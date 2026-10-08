using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.AI;
using Studio.Api;
using Studio.Crew;
using Studio.Engine;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<JsonOptions>(o => StudioJson.Configure(o.SerializerOptions));
builder.Services.AddSingleton<MatchLibrary>();
var crewOptions = builder.Configuration.GetSection("Crew").Get<CrewOptions>() ?? new CrewOptions();
builder.Services.AddSingleton(crewOptions);
builder.Services.AddSingleton<IChatClient>(_ => ModelFactory.Create(crewOptions));
builder.Services.AddHttpClient("speech");
builder.Services.AddSingleton(sp => new RecapVoice(sp.GetRequiredService<IHttpClientFactory>().CreateClient("speech"), crewOptions));
builder.Services.AddSingleton<RecapService>();
builder.Services.AddSingleton<UsageGuard>();
builder.Services.AddSingleton<CommentaryService>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseWebSockets();

// In the container the built frontend ships in wwwroot, so one origin serves the app, API and socket.
var serveFrontend = app.Environment.WebRootPath is { } webRoot && Directory.Exists(webRoot);
if (serveFrontend)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapGet("/api/health", (CrewOptions crew) => Results.Ok(new
{
    status = "ok",
    version = typeof(MatchPipeline).Assembly.GetName().Version?.ToString(),
    crew = crew.IsAzure ? $"azure:{crew.Deployment}" : "mock",
    voices = !string.IsNullOrWhiteSpace(crew.SpeechRegion) && crew.IsAzure,
}));

var matches = app.MapGroup("/api/matches/{seed:int}");
matches.MapGet("/", (int seed, MatchLibrary lib) => lib.Get(seed).Info);
matches.MapGet("/events", (int seed, MatchLibrary lib) => lib.Get(seed).Events);
matches.MapGet("/summary", (int seed, MatchLibrary lib) => lib.Summary(seed));

// Graphics partners: the match's overlays as a timed, layered timeline (docs/overlay-timeline.schema.json).
matches.MapGet("/overlays", (int seed, string? viewer, MatchLibrary lib) =>
    viewer is not null && ViewerProfile.Parse(viewer) is null
        ? Results.BadRequest("Unknown viewer. Use a profile such as analyst.en, casual.es, club.fr.home, player.en.H10 or metric.en.pressing.")
        : Results.Ok(OverlayTimeline.Build(lib.Summary(seed), viewer is null ? null : ViewerProfile.Parse(viewer))));

// Full-time recap: a short crew conversation in the viewer's language, checked by Ref, plus its audio.
matches.MapGet("/recap", async (int seed, string? lang, RecapService recaps) =>
{
    if (await recaps.GetAsync(seed, Language(lang)) is not { } result)
        return Results.Problem("The crew has written its limit of new recaps this hour. Try a match that's already been recapped, or come back later.", statusCode: 429);
    return Results.Ok(new
    {
        result.Script,
        hasAudio = result.Audio is not null,
        result.AudioError,
        voices = result.Script.Lines.Select(l => RecapVoice.VoiceFor(result.Script.Language, l.Speaker)),
    });
});
matches.MapGet("/recap/audio", async (int seed, string? lang, RecapService recaps) =>
    (await recaps.GetAsync(seed, Language(lang)))?.Audio is { } audio ? Results.File(audio, "audio/mpeg") : Results.NotFound());

// Live commentary: every line for a match, and the voice for the big moments (server-written lines only).
matches.MapGet("/commentary", (int seed, string? lang, CommentaryService commentary) =>
    commentary.Lines(seed, Language(lang)).Values.OrderBy(l => l.Seq));
matches.MapGet("/commentary/{seq:int}/audio", async (int seed, int seq, string? lang, CommentaryService commentary) =>
    await commentary.AudioAsync(seed, Language(lang), seq) is { } audio ? Results.File(audio, "audio/mpeg") : Results.NotFound());

// Live replay: streams the match as if it were happening, `speed` times faster than real time,
// or `mode=condensed` for a self-paced condensed match (about 4-5 minutes).
// `crew=off` streams template cards only. `viewers=analyst.en,casual.es,club.fr.home,player.en.H10`
// adds a personalised version of every card for each viewer (up to four).
app.Map("/ws/replay", async (HttpContext ctx, MatchLibrary lib, IChatClient chat, CrewOptions crewOptions,
    CommentaryService commentary, UsageGuard usage, ILogger<ReplayStreamer> logger, IHostApplicationLifetime lifetime) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
        return Results.BadRequest("Expected a WebSocket request.");

    var seed = int.TryParse(ctx.Request.Query["seed"], out var s) ? s : 7;
    var speed = double.TryParse(ctx.Request.Query["speed"], out var sp) ? Math.Clamp(sp, 1, 10_000) : 10;
    var condensed = ctx.Request.Query["mode"] == "condensed";
    var wantsCrew = ctx.Request.Query["crew"] != "off";
    using var crewSlot = wantsCrew ? usage.TryStartCrewReplay() : null;
    var crew = crewSlot is null ? null : new StudioCrew(chat, crewOptions, highlightsOnly: condensed);
    var viewers = ViewerProfile.ParseList(ctx.Request.Query["viewers"]);
    var languages = viewers.Select(v => v.Language).Append("en").Distinct();
    var lines = languages.ToDictionary(l => l, l => commentary.Lines(seed, l));
    var notice = wantsCrew && crew is null
        ? "The live crew is busy with other viewers right now, so this replay shows template graphics. Try again in a few minutes."
        : null;
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, lifetime.ApplicationStopping);
    // Read from the socket so pings get answered and a viewer leaving stops the replay (and the crew) at once.
    _ = Task.Run(async () =>
    {
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
                if ((await socket.ReceiveAsync(buffer, cts.Token)).MessageType == WebSocketMessageType.Close) break;
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        await cts.CancelAsync();
    });
    try
    {
        await new ReplayStreamer(lib.Get(seed), speed, crew, viewers, lines, notice, logger, condensed).StreamAsync(
            (envelope, ct) => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, StudioJson.Options),
                WebSocketMessageType.Text, true, ct),
            cts.Token);
        // The read loop above owns receiving, so only send our half of the close handshake.
        if (socket.State == WebSocketState.Open)
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "full time", CancellationToken.None);
    }
    catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
    {
        // The viewer left mid-match; nothing to do.
    }
    return Results.Empty;
});

if (serveFrontend) app.MapFallbackToFile("index.html");

app.Run();

static string Language(string? lang) => ViewerProfile.Languages.Contains(lang) ? lang! : "en";

public partial class Program;

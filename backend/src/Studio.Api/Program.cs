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

// Full-time recap: a short crew conversation in the viewer's language, checked by Ref, plus its audio.
matches.MapGet("/recap", async (int seed, string? lang, RecapService recaps) =>
{
    var result = await recaps.GetAsync(seed, Language(lang));
    return new
    {
        result.Script,
        hasAudio = result.Audio is not null,
        result.AudioError,
        voices = result.Script.Lines.Select(l => RecapVoice.VoiceFor(result.Script.Language, l.Speaker)),
    };
});
matches.MapGet("/recap/audio", async (int seed, string? lang, RecapService recaps) =>
    (await recaps.GetAsync(seed, Language(lang))).Audio is { } audio ? Results.File(audio, "audio/mpeg") : Results.NotFound());

// Live replay: streams the match as if it were happening, `speed` times faster than real time.
// `crew=off` streams template cards only. `viewers=analyst.en,casual.es,club.fr.home,player.en.H10`
// adds a personalised version of every card for each viewer (up to four).
app.Map("/ws/replay", async (HttpContext ctx, MatchLibrary lib, IChatClient chat, CrewOptions crewOptions,
    ILogger<ReplayStreamer> logger, IHostApplicationLifetime lifetime) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
        return Results.BadRequest("Expected a WebSocket request.");

    var seed = int.TryParse(ctx.Request.Query["seed"], out var s) ? s : 7;
    var speed = double.TryParse(ctx.Request.Query["speed"], out var sp) ? Math.Clamp(sp, 1, 10_000) : 20;
    var crew = ctx.Request.Query["crew"] == "off" ? null : new StudioCrew(chat, crewOptions);
    var viewers = ViewerProfile.ParseList(ctx.Request.Query["viewers"]);
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, lifetime.ApplicationStopping);
    await new ReplayStreamer(lib.Get(seed), speed, crew, viewers, logger).StreamAsync(
        (envelope, ct) => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, StudioJson.Options),
            WebSocketMessageType.Text, true, ct),
        cts.Token);
    if (socket.State == WebSocketState.Open)
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "full time", CancellationToken.None);
    return Results.Empty;
});

if (serveFrontend) app.MapFallbackToFile("index.html");

app.Run();

static string Language(string? lang) => ViewerProfile.Languages.Contains(lang) ? lang! : "en";

public partial class Program;

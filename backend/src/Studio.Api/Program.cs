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
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseWebSockets();

app.MapGet("/api/health", (CrewOptions crew) => Results.Ok(new
{
    status = "ok",
    version = typeof(MatchPipeline).Assembly.GetName().Version?.ToString(),
    crew = crew.IsAzure ? $"azure:{crew.Deployment}" : "mock",
}));

var matches = app.MapGroup("/api/matches/{seed:int}");
matches.MapGet("/", (int seed, MatchLibrary lib) => lib.Get(seed).Info);
matches.MapGet("/events", (int seed, MatchLibrary lib) => lib.Get(seed).Events);
matches.MapGet("/summary", (int seed, MatchLibrary lib) => lib.Summary(seed));

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

app.Run();

public partial class Program;

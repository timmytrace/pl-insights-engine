using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Studio.Api;
using Studio.Engine;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<JsonOptions>(o => StudioJson.Configure(o.SerializerOptions));
builder.Services.AddSingleton<MatchLibrary>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseWebSockets();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", version = typeof(MatchPipeline).Assembly.GetName().Version?.ToString() }));

var matches = app.MapGroup("/api/matches/{seed:int}");
matches.MapGet("/", (int seed, MatchLibrary lib) => lib.Get(seed).Info);
matches.MapGet("/events", (int seed, MatchLibrary lib) => lib.Get(seed).Events);
matches.MapGet("/summary", (int seed, MatchLibrary lib) => lib.Summary(seed));

// Live replay: streams the match as if it were happening, `speed` times faster than real time.
app.Map("/ws/replay", async (HttpContext ctx, MatchLibrary lib, IHostApplicationLifetime lifetime) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
        return Results.BadRequest("Expected a WebSocket request.");

    var seed = int.TryParse(ctx.Request.Query["seed"], out var s) ? s : 7;
    var speed = double.TryParse(ctx.Request.Query["speed"], out var sp) ? Math.Clamp(sp, 1, 10_000) : 20;
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted, lifetime.ApplicationStopping);
    await new ReplayStreamer(lib.Get(seed), speed).StreamAsync(
        (envelope, ct) => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, StudioJson.Options),
            WebSocketMessageType.Text, true, ct),
        cts.Token);
    if (socket.State == WebSocketState.Open)
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "full time", CancellationToken.None);
    return Results.Empty;
});

app.Run();

public partial class Program;

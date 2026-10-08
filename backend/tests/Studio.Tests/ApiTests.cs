using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Studio.Tests;

public class ApiTests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    // Tests always use the offline model, whatever the developer's user secrets say: no test spends Azure credits.
    private readonly WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Crew:Mode", "mock");
        b.UseSetting("Crew:MockLatencyMs", "0");
    });

    [Fact]
    public async Task Health_is_ok()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Summary_returns_full_time_stats_and_cards()
    {
        var json = await factory.CreateClient().GetStringAsync("/api/matches/7/summary");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("SYN-00007", doc.RootElement.GetProperty("info").GetProperty("matchId").GetString());
        Assert.True(doc.RootElement.GetProperty("cards").GetArrayLength() > 0);
        Assert.Equal("home", doc.RootElement.GetProperty("info").GetProperty("home").GetProperty("side").GetString());
    }

    [Fact]
    public async Task Replay_socket_streams_the_whole_match()
    {
        var client = factory.Server.CreateWebSocketClient();
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws/replay?seed=7&speed=10000"), CancellationToken.None);

        var types = new List<string>();
        var buffer = new byte[1 << 16];
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            if (result.MessageType == WebSocketMessageType.Close) break;
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(message.ToArray()));
            types.Add(doc.RootElement.GetProperty("type").GetString()!);
        }

        Assert.Equal("info", types[0]);
        Assert.Equal("end", types[^1]);
        Assert.Contains("card", types);
        Assert.Contains("snapshot", types);
        Assert.Contains("crew", types);
        Assert.Equal(Studio.Engine.MatchSimulator.Simulate(7).Events.Count, types.Count(t => t == "event"));
    }
}

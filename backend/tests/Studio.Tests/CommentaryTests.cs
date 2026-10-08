using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Studio.Api;
using Studio.Crew;
using Studio.Engine;

namespace Studio.Tests;

public class CommentaryTests
{
    private static (Match Match, List<CommentaryLine> Lines) Call(int seed, string lang)
    {
        var match = MatchSimulator.Simulate(seed);
        var pipeline = new MatchPipeline(match.Info);
        var commentator = new Commentator(match.Info, lang);
        var lines = new List<CommentaryLine>();
        foreach (var e in match.Events)
        {
            pipeline.Process(e);
            if (commentator.Call(e, pipeline.State) is { } l) lines.Add(l);
        }
        return (match, lines);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    public void Every_shot_and_period_is_called_and_voiced(string lang)
    {
        var (match, lines) = Call(7, lang);
        var shots = match.Events.Where(e => e.Type == EventType.Shot).Select(e => e.Id).ToHashSet();
        var called = lines.Where(l => l.Voiced).Select(l => l.EventId).ToHashSet();
        Assert.Subset(called, shots);
        Assert.Equal(2, lines.Count(l => l.Voiced && match.Events.First(e => e.Id == l.EventId).Type == EventType.PeriodEnd));
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Text)));
    }

    [Fact]
    public void Goal_calls_carry_the_new_score()
    {
        var (match, lines) = Call(7, "en");
        var firstGoal = match.Events.First(e => e is { Type: EventType.Shot, Outcome: "goal" });
        var line = lines.Single(l => l.EventId == firstGoal.Id);
        Assert.StartsWith("GOAL!", line.Text);
        Assert.Equal(3, line.Excitement);
        var score = firstGoal.Team == Side.Home ? "1–0" : "0–1";
        Assert.Contains(score, line.Text);
    }

    [Fact]
    public void Routine_lines_are_spaced_out_so_the_commentary_breathes()
    {
        var (_, lines) = Call(7, "en");
        var routine = lines.Where(l => !l.Voiced && l.Excitement < 3).ToList();
        Assert.True(routine.Count > 20);
        Assert.True(lines.Count < 400, $"{lines.Count} lines is a wall of text");
    }

    [Fact]
    public void Commentary_is_deterministic()
    {
        Assert.Equal(Call(3, "es").Lines.Select(l => l.Text), Call(3, "es").Lines.Select(l => l.Text));
    }

    [Fact]
    public void Line_ssml_escapes_text_and_lifts_big_moments()
    {
        var ssml = RecapVoice.LineSsml("GOAL! 1 & 0 <wow>", "en", 3);
        Assert.Contains("1 &amp; 0 &lt;wow&gt;", ssml);
        Assert.Contains("rate=\"+20%\"", ssml);
        Assert.Contains("en-GB-SoniaNeural", ssml);
    }

    [Fact]
    public void Usage_guard_caps_live_crews_and_hourly_work()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Usage:MaxConcurrentCrewReplays"] = "1",
            ["Usage:SpeechCallsPerHour"] = "2",
        }).Build();
        var guard = new UsageGuard(config);
        var slot = guard.TryStartCrewReplay();
        Assert.NotNull(slot);
        Assert.Null(guard.TryStartCrewReplay());
        slot!.Dispose();
        Assert.NotNull(guard.TryStartCrewReplay());
        Assert.True(guard.TrySpeech());
        Assert.True(guard.TrySpeech());
        Assert.False(guard.TrySpeech());
    }
}

public class CommentaryApiTests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Crew:Mode", "mock");
        b.UseSetting("Crew:MockLatencyMs", "0");
    });

    [Fact]
    public async Task Commentary_lines_are_served_and_audio_is_off_offline()
    {
        var client = factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/matches/7/commentary?lang=es"));
        var first = doc.RootElement[0];
        Assert.Equal("es", first.GetProperty("language").GetString());
        var seq = first.GetProperty("seq").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/matches/7/commentary/{seq}/audio?lang=es")).StatusCode);
    }
}

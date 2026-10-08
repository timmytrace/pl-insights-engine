using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Studio.Crew;
using Studio.Engine;

namespace Studio.Tests;

public class RecapTests
{
    private static (MatchInfo Info, FactSheet Sheet) FullTime(int seed)
    {
        var match = MatchSimulator.Simulate(seed);
        var pipeline = new MatchPipeline(match.Info);
        var moments = match.Events.SelectMany(e => pipeline.Process(e).Moments).ToList();
        return (match.Info, RecapWriter.FullTimeSheet(pipeline.State, moments));
    }

    [Fact]
    public void The_full_time_sheet_tells_the_story_in_order()
    {
        var (_, sheet) = FullTime(7);
        Assert.True(sheet.Has("home_score") && sheet.Has("away_score") && sheet.Has("star_1_name"));
        var minutes = Enumerable.Range(1, 8).Select(i => sheet.Get($"moment_{i}_minute")?.Number).OfType<double>().ToList();
        Assert.NotEmpty(minutes);
        Assert.Equal(minutes.Order(), minutes);
        Assert.Contains(sheet.Facts, f => f.Key.StartsWith("moment_") && f.Value is string s && s.StartsWith("goal"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    public async Task The_recap_is_verified_in_every_language(string lang)
    {
        var (info, sheet) = FullTime(7);
        var script = await new RecapWriter(new ScriptedChatClient(0)).WriteAsync(sheet, info.MatchId, lang, CancellationToken.None);
        Assert.True(script.Verified, string.Join(" ", script.Reasons));
        Assert.Equal("host", script.Lines[0].Speaker);
        Assert.Contains(script.Lines, l => l.Speaker == "ref");
        Assert.All(script.Lines, l => Assert.Contains(l.Speaker, RecapWriter.Speakers));
        Assert.True(script.NumbersChecked > 0);
    }

    [Fact]
    public void Ssml_gives_each_character_their_voice_and_escapes_text()
    {
        var script = new RecapScript("M", "es", [new("host", "¡Gol! 1 & 0 <final>"), new("gaffer", "Presión alta.")], true, [], 2, 1);
        var ssml = RecapVoice.Ssml(script);
        Assert.Contains("xml:lang=\"es-ES\"", ssml);
        Assert.Contains("es-ES-ElviraNeural", ssml);
        Assert.Contains("es-ES-AlvaroNeural", ssml);
        Assert.Contains("1 &amp; 0 &lt;final&gt;", ssml);
    }

    [Fact]
    public void Voices_are_off_without_azure_speech_settings()
    {
        Assert.False(new RecapVoice(new HttpClient(), new CrewOptions()).Enabled);
        Assert.False(new RecapVoice(new HttpClient(), new CrewOptions { Mode = "azure", SpeechRegion = "eastus" }).Enabled);
    }
}

public class RecapApiTests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Crew:Mode", "mock");
        b.UseSetting("Crew:MockLatencyMs", "0");
    });

    [Fact]
    public async Task Recap_returns_a_verified_script_without_audio_offline()
    {
        var client = factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/matches/7/recap?lang=fr"));
        var script = doc.RootElement.GetProperty("script");
        Assert.True(script.GetProperty("verified").GetBoolean());
        Assert.Equal("fr", script.GetProperty("language").GetString());
        Assert.False(doc.RootElement.GetProperty("hasAudio").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/matches/7/recap/audio?lang=fr")).StatusCode);
    }

    [Fact]
    public async Task Unknown_languages_fall_back_to_english()
    {
        using var doc = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/api/matches/7/recap?lang=xx"));
        Assert.Equal("en", doc.RootElement.GetProperty("script").GetProperty("language").GetString());
    }
}

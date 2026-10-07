using System.Text.Json;
using Studio.Engine;

namespace Studio.Tests;

/// <summary>The committed sample dataset must be exactly what the generator produces today.</summary>
public class DatasetTests
{
    private static string SampleDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", "sample", "seed-7");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("data/sample/seed-7 not found above the test output folder");
    }

    [Fact]
    public void Sample_events_match_the_generator()
    {
        var expected = MatchSimulator.Simulate(7).Events.Select(e => JsonSerializer.Serialize(e, StudioJson.Options)).ToList();
        var actual = File.ReadAllLines(Path.Combine(SampleDir(), "events.jsonl")).ToList();
        Assert.True(expected.SequenceEqual(actual),
            "data/sample/seed-7 is stale. Regenerate with: dotnet run --project backend/src/Studio.Cli -- generate --seed 7 --out data/sample/seed-7");
    }

    [Fact]
    public void Sample_events_round_trip_through_the_schema()
    {
        var line = File.ReadLines(Path.Combine(SampleDir(), "events.jsonl")).First();
        var e = JsonSerializer.Deserialize<MatchEvent>(line, StudioJson.Options)!;
        Assert.Equal(EventType.Kickoff, e.Type);
        Assert.Equal("SYN-00007-00001", e.Id);
    }
}

using System.Text.Json;
using Studio.Engine;

namespace Studio.Tests;

public class SimulatorTests
{
    [Fact]
    public void Same_seed_produces_the_same_match()
    {
        var a = JsonSerializer.Serialize(MatchSimulator.Simulate(7), StudioJson.Options);
        var b = JsonSerializer.Serialize(MatchSimulator.Simulate(7), StudioJson.Options);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_seeds_produce_different_matches()
    {
        Assert.NotEqual(MatchSimulator.Simulate(1).Events.Count, MatchSimulator.Simulate(2).Events.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Events_are_ordered_and_on_the_pitch(int seed)
    {
        var match = MatchSimulator.Simulate(seed);
        var ids = match.Info.Home.Starters.Concat(match.Info.Home.Bench)
            .Concat(match.Info.Away.Starters).Concat(match.Info.Away.Bench).Select(p => p.Id).ToHashSet();

        for (var i = 1; i < match.Events.Count; i++)
        {
            Assert.Equal(match.Events[i - 1].Seq + 1, match.Events[i].Seq);
            Assert.True(match.Events[i].T >= match.Events[i - 1].T, $"time went backwards at {match.Events[i].Id}");
        }
        foreach (var e in match.Events)
        {
            if (e.X is { } x) Assert.InRange(x, 0, Pitch.Length);
            if (e.Y is { } y) Assert.InRange(y, 0, Pitch.Width);
            if (e.PlayerId is { } pid) Assert.Contains(pid, ids);
        }
    }

    [Fact]
    public void A_match_has_two_halves_of_at_least_45_minutes()
    {
        var match = MatchSimulator.Simulate(7);
        var ends = match.Events.Where(e => e.Type == EventType.PeriodEnd).ToList();
        Assert.Equal(2, ends.Count);
        Assert.True(ends[0].Clock >= 45 * 60);
        Assert.True(ends[1].Clock >= 90 * 60);
        Assert.Equal(2, match.Events.Count(e => e.Type == EventType.Kickoff && e.Clock is 0 or 2700));
    }

    [Fact]
    public void Substitutions_happen_in_the_second_half_only()
    {
        var subs = MatchSimulator.Simulate(7).Events.Where(e => e.Type == EventType.Substitution).ToList();
        Assert.NotEmpty(subs);
        Assert.All(subs, s => Assert.Equal(2, s.Period));
    }

    [Fact]
    public void Season_averages_look_like_real_football()
    {
        var teams = Enumerable.Range(1, 20).SelectMany(seed =>
        {
            var match = MatchSimulator.Simulate(seed);
            var state = new MatchState(match.Info);
            foreach (var e in match.Events) state.Apply(e);
            var snap = state.Snapshot();
            return new[] { snap.Home, snap.Away };
        }).ToList();

        Assert.InRange(teams.Average(t => t.Goals), 0.8, 2.2);
        Assert.InRange(teams.Average(t => t.Shots), 7, 16);
        Assert.InRange(teams.Average(t => t.Passes), 400, 700);
        Assert.InRange(teams.Average(t => t.PassAccuracyPct), 74, 88);
        Assert.InRange(teams.Average(t => t.Xg), 0.8, 2.4);
    }
}

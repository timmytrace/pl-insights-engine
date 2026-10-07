using Studio.Engine;

namespace Studio.Tests;

public class MatchStateTests
{
    private static readonly MatchInfo Info = MatchSimulator.Simulate(7).Info;
    private int _seq;

    private MatchEvent Ev(EventType type, Side team, double t, string? player = null, string? outcome = null,
        double? x = null, double? y = null, double? endX = null, double? endY = null, string? receiver = null,
        double? xg = null, double? distance = null) => new()
    {
        Id = $"T-{++_seq}", Seq = _seq, T = t, Clock = t, Period = 1, Type = type, Team = team,
        Possession = 1, PlayerId = player, Outcome = outcome, X = x, Y = y, EndX = endX, EndY = endY,
        ReceiverId = receiver, Xg = xg, DistanceM = distance,
    };

    [Fact]
    public void Full_match_totals_are_consistent()
    {
        var match = MatchSimulator.Simulate(7);
        var state = new MatchState(match.Info);
        foreach (var e in match.Events) state.Apply(e);
        var snap = state.Snapshot();

        Assert.Equal(100, snap.Home.PossessionPct + snap.Away.PossessionPct, precision: 0);
        Assert.Equal(match.Events.Count(e => e is { Type: EventType.Shot, Outcome: "goal", Team: Side.Home }), snap.HomeScore);
        Assert.Equal(match.Events.Count(e => e is { Type: EventType.Shot, Outcome: "goal", Team: Side.Away }), snap.AwayScore);
        Assert.Equal(snap.HomeScore, snap.Players.Where(p => p.Side == Side.Home).Sum(p => p.Goals));
        Assert.Equal(22 + match.Events.Count(e => e.Type == EventType.Substitution), snap.Players.Count);
        Assert.Equal(22, snap.Players.Count(p => p.OnPitch));
    }

    [Fact]
    public void A_pass_that_sets_up_a_shot_is_credited_as_a_key_pass()
    {
        var state = new MatchState(Info);
        state.Apply(Ev(EventType.Kickoff, Side.Home, 0, "H10", x: 52.5, y: 34));
        state.Apply(Ev(EventType.Pass, Side.Home, 3, "H08", "complete", 70, 30, 92, 34, "H10", distance: 22.4));
        var shot = state.Apply(Ev(EventType.Shot, Side.Home, 5, "H10", "saved", 92, 34, 105, 34, xg: 0.12));

        Assert.True(shot.KeyPassCredit);
        Assert.Equal("H08", shot.KeyPasserId);
        Assert.Equal(1, state.Player("H08")!.KeyPasses);
    }

    [Fact]
    public void Pass_difficulty_is_computed_by_the_engine()
    {
        var state = new MatchState(Info);
        var m = state.Apply(Ev(EventType.Pass, Side.Home, 1, "H06", "complete", 40, 34, 80, 40, "H10", distance: 40.4));
        Assert.Equal(Math.Round(Pitch.PassDifficulty(40, 34, 80, 40, false), 3), m.PassDifficulty);
        Assert.True(m.Progressive);
    }

    [Fact]
    public void Ppda_counts_opponent_build_up_passes_per_defensive_action()
    {
        var state = new MatchState(Info);
        // Away passes in their own half (home frame x 70 = away frame x 35), then home wins it back high.
        for (var i = 0; i < 6; i++)
            state.Apply(Ev(EventType.Pass, Side.Away, i * 3, "A06", "complete", 70, 34, 65, 30, "A07", distance: 6));
        state.Apply(Ev(EventType.Tackle, Side.Home, 20, "H09", "won", 72, 34));
        state.Apply(Ev(EventType.Interception, Side.Home, 25, "H07", "won", 68, 30));

        Assert.Equal(3.0, state.FullMatchPpda(Side.Home));
        Assert.Null(state.FullMatchPpda(Side.Away));
    }
}

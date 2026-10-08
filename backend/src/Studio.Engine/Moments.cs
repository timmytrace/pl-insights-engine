namespace Studio.Engine;

public enum MomentKind
{
    Goal,
    BigChance,
    ShotSpeed,
    SprintSpeed,
    ElitePass,
    Milestone,
    PressSurge,
    MomentumSwing,
    ChaosSpell,
    ControlSpell,
}

/// <summary>
/// A candidate "this matters" moment. Facts are the numbers behind it and Evidence lists the
/// event ids that led up to it, so anything said about the moment can be traced to data.
/// </summary>
public sealed record Moment(
    string Id,
    MomentKind Kind,
    double T,
    double Clock,
    int Minute,
    Side Team,
    string? PlayerId,
    string? PlayerName,
    double Severity,
    IReadOnlyList<string> Evidence,
    IReadOnlyDictionary<string, object> Facts);

/// <summary>Predefined thresholds that trigger on-screen indicators.</summary>
public static class Thresholds
{
    public const double SprintSpeedKmh = 32;
    public const double ShotSpeedKmh = 100;
    public const double ElitePassDifficulty = 0.7;
    public const double BigChanceXg = 0.25;
    public const double MomentumSwing = 45;
    public const double Chaos = 65;
    public const double Control = 72;
    public static readonly int[] PassMilestones = [50, 75, 100];
    public static readonly int[] DistanceMilestonesKm = [5, 10];
}

/// <summary>
/// Rule-based detector: decides *that* something matters. Explaining *why* is the agents' job,
/// working only from the facts attached here.
/// </summary>
public sealed class MomentDetector(MatchState state)
{
    private const double WarmUp = 600;          // live indicators need ten minutes of data
    private const double LiveCheckEvery = 30;

    private readonly Queue<MatchEvent> _chain = new();
    private readonly Dictionary<(MomentKind, Side), double> _lastFired = [];
    private double _lastLiveCheck;
    private double _momentumExtreme;   // most extreme momentum since the last swing
    private readonly HashSet<(Side Team, int Km)> _distanceMarks = [];
    private int _count;

    public IReadOnlyList<Moment> Observe(EventMetrics m)
    {
        var e = m.Event;
        _chain.Enqueue(e);
        while (_chain.Count > 8) _chain.Dequeue();

        var found = new List<Moment>();
        var player = state.Player(e.PlayerId);

        switch (e.Type)
        {
            case EventType.Shot when e.Outcome == "goal":
                var scorer = player!;
                found.Add(Make(MomentKind.Goal, e, 1.0, Facts(
                    ("xg", e.Xg ?? 0), ("shotSpeedKmh", e.BallSpeedKmh ?? 0), ("distanceM", e.DistanceM ?? 0),
                    ("bodyPart", e.BodyPart ?? "foot"), ("homeScore", state.HomeScore), ("awayScore", state.AwayScore),
                    ("assistId", m.KeyPasserId ?? ""), ("assistName", state.Player(m.KeyPasserId)?.Player.Name ?? ""))));
                if (scorer.Goals is 2 or 3)
                    found.Add(Make(MomentKind.Milestone, e, scorer.Goals == 3 ? 0.9 : 0.7, Facts(
                        ("milestone", scorer.Goals == 3 ? "hat_trick" : "brace"), ("goals", scorer.Goals))));
                break;

            case EventType.Shot:
                if (e.Xg >= Thresholds.BigChanceXg)
                    found.Add(Make(MomentKind.BigChance, e, 0.8, Facts(
                        ("xg", e.Xg ?? 0), ("outcome", e.Outcome ?? ""), ("distanceM", e.DistanceM ?? 0),
                        ("underPressure", e.UnderPressure))));
                break;

            case EventType.Sprint when e.PlayerSpeedKmh >= Thresholds.SprintSpeedKmh:
                found.Add(Make(MomentKind.SprintSpeed, e, 0.4, Facts(
                    ("speedKmh", e.PlayerSpeedKmh ?? 0), ("distanceM", e.DistanceM ?? 0),
                    ("thresholdKmh", Thresholds.SprintSpeedKmh), ("matchTopSpeedKmh", player?.TopSpeedKmh ?? 0))));
                break;

            case EventType.Pass when e.Outcome == "complete":
                if (m.PassDifficulty >= Thresholds.ElitePassDifficulty)
                    found.Add(Make(MomentKind.ElitePass, e, 0.5, Facts(
                        ("difficulty", m.PassDifficulty ?? 0), ("distanceM", e.DistanceM ?? 0),
                        ("ballSpeedKmh", e.BallSpeedKmh ?? 0), ("underPressure", e.UnderPressure),
                        ("progressive", m.Progressive), ("receiverName", state.Player(e.ReceiverId)?.Player.Name ?? ""))));
                if (player is not null && Thresholds.PassMilestones.Contains(player.PassesCompleted))
                    found.Add(Make(MomentKind.Milestone, e, 0.3, Facts(
                        ("milestone", "passes_completed"), ("passesCompleted", player.PassesCompleted),
                        ("accuracyPct", Math.Round(100.0 * player.PassesCompleted / player.Passes, 1)))));
                break;
        }

        // Shot speed is independent of outcome: a rocket that hits the bar still gets a tag.
        if (e.Type == EventType.Shot && e.BallSpeedKmh >= Thresholds.ShotSpeedKmh)
            found.Add(Make(MomentKind.ShotSpeed, e, 0.5, Facts(
                ("shotSpeedKmh", e.BallSpeedKmh ?? 0), ("distanceM", e.DistanceM ?? 0),
                ("thresholdKmh", Thresholds.ShotSpeedKmh), ("outcome", e.Outcome ?? ""))));

        if (e.T >= WarmUp && e.T - _lastLiveCheck >= LiveCheckEvery)
        {
            _lastLiveCheck = e.T;
            found.AddRange(LiveMoments(e));
        }
        return found;
    }

    private IEnumerable<Moment> LiveMoments(MatchEvent e)
    {
        var snapshot = state.Snapshot();
        var live = snapshot.Live;

        // Distance indicators: a tag for the first player on each team to pass each threshold.
        foreach (var p in snapshot.Players.Where(p => p.OnPitch).OrderByDescending(p => p.DistanceKm))
            foreach (var km in Thresholds.DistanceMilestonesKm.Where(km => p.DistanceKm >= km && _distanceMarks.Add((p.Side, km))))
                yield return new Moment($"{state.Info.MatchId}-M{++_count:D3}", MomentKind.Milestone, e.T, e.Clock, e.Minute,
                    p.Side, p.Id, p.Name, 0.3, _chain.Select(c => c.Id).ToList(),
                    Facts(("milestone", "distance"), ("thresholdKm", km), ("distanceKm", p.DistanceKm), ("firstOnTeam", true)));

        foreach (var side in new[] { Side.Home, Side.Away })
        {
            var ppda10 = side == Side.Home ? live.HomePpda10 : live.AwayPpda10;
            var matchPpda = state.FullMatchPpda(side);
            if (ppda10 is { } p && matchPpda is { } mp && p <= 8 && p <= 0.6 * mp && Ready(MomentKind.PressSurge, side, 900, e.T))
                yield return Make(MomentKind.PressSurge, e, 0.7, Facts(
                    ("ppda10", p), ("matchPpda", mp), ("pressures", state.Team(side).Pressures)), side, withPlayer: false);

            var control = side == Side.Home ? live.HomeControl : live.AwayControl;
            if (control >= Thresholds.Control && Ready(MomentKind.ControlSpell, side, 600, e.T))
                yield return Make(MomentKind.ControlSpell, e, 0.5, Facts(
                    ("control", control), ("tempoPassesPerMin", live.TempoPassesPerMin)), side, withPlayer: false);
        }

        // A swing: momentum crosses to the other side by a clear margin after a spell of dominance.
        var mom = live.Momentum;
        if (Math.Abs(mom) > Math.Abs(_momentumExtreme) && Math.Sign(mom) == Math.Sign(_momentumExtreme))
            _momentumExtreme = mom;
        if (Math.Abs(mom) >= Thresholds.MomentumSwing
            && Math.Abs(_momentumExtreme) >= Thresholds.MomentumSwing
            && Math.Sign(mom) != Math.Sign(_momentumExtreme))
        {
            var side = mom > 0 ? Side.Home : Side.Away;
            if (Ready(MomentKind.MomentumSwing, side, 480, e.T))
                yield return Make(MomentKind.MomentumSwing, e, 0.7, Facts(
                    ("momentum", mom), ("previousMomentum", _momentumExtreme),
                    ("turnoversPerMin", live.TurnoversPerMin)), side, withPlayer: false);
            _momentumExtreme = mom;
        }
        else if (_momentumExtreme == 0)
        {
            _momentumExtreme = mom;
        }

        if (live.Chaos >= Thresholds.Chaos && Ready(MomentKind.ChaosSpell, Side.Home, 600, e.T))
            yield return Make(MomentKind.ChaosSpell, e, 0.6, Facts(
                ("chaos", live.Chaos), ("turnoversPerMin", live.TurnoversPerMin),
                ("tempoPassesPerMin", live.TempoPassesPerMin)), Side.Home, withPlayer: false);
    }

    private bool Ready(MomentKind kind, Side side, double cooldown, double t)
    {
        if (_lastFired.TryGetValue((kind, side), out var last) && t - last < cooldown) return false;
        _lastFired[(kind, side)] = t;
        return true;
    }

    private static Dictionary<string, object> Facts(params (string Key, object Value)[] items) =>
        items.ToDictionary(i => i.Key, i => i.Value is double d ? Math.Round(d, 3) : i.Value);

    private Moment Make(MomentKind kind, MatchEvent e, double severity, Dictionary<string, object> facts,
        Side? team = null, bool withPlayer = true)
    {
        var player = withPlayer ? state.Player(e.PlayerId) : null;
        return new Moment(
            $"{state.Info.MatchId}-M{++_count:D3}", kind, e.T, e.Clock, e.Minute, team ?? e.Team,
            player?.Player.Id, player?.Player.Name, severity, _chain.Select(c => c.Id).ToList(), facts);
    }
}

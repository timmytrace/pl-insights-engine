namespace Studio.Engine;

/// <summary>Metrics the engine derives for a single event. The raw event never carries these.</summary>
public sealed record EventMetrics(
    MatchEvent Event,
    double? PassDifficulty = null,
    bool Progressive = false,
    bool KeyPassCredit = false,
    string? KeyPasserId = null);

public sealed class TeamStats
{
    public int Passes { get; set; }
    public int PassesCompleted { get; set; }
    public double PassDistance { get; set; }
    public double DifficultySum { get; set; }
    public int ProgressivePasses { get; set; }
    public int Shots { get; set; }
    public int ShotsOnTarget { get; set; }
    public int Goals { get; set; }
    public double Xg { get; set; }
    public int Tackles { get; set; }
    public int Interceptions { get; set; }
    public int Pressures { get; set; }
    public int Fouls { get; set; }
    public double PossessionSeconds { get; set; }
    public int Possessions { get; set; }
}

public sealed class PlayerStats(Player player, Side side, double onClock)
{
    public Player Player { get; } = player;
    public Side Side { get; } = side;
    public double OnClock { get; set; } = onClock;
    public double? OffClock { get; set; }
    public int Touches { get; set; }
    public int Passes { get; set; }
    public int PassesCompleted { get; set; }
    public double DifficultySum { get; set; }
    public int KeyPasses { get; set; }
    public int Shots { get; set; }
    public int Goals { get; set; }
    public double Xg { get; set; }
    public int Tackles { get; set; }
    public int Interceptions { get; set; }
    public int Pressures { get; set; }
    public int Sprints { get; set; }
    public double TopSpeedKmh { get; set; }
    public double SprintDistance { get; set; }
    public double MaxShotSpeedKmh { get; set; }
}

public sealed record TeamSnapshot(
    double PossessionPct, int Passes, int PassesCompleted, double PassAccuracyPct,
    double AvgPassDistanceM, double AvgPassDifficulty, int ProgressivePasses, int Shots,
    int ShotsOnTarget, int Goals, double Xg, int Tackles, int Interceptions, int Pressures,
    int Fouls, double? Ppda);

/// <summary>Rolling indicators over the recent window: these drive the "why it matters" layer.</summary>
public sealed record LiveSnapshot(
    double Momentum,          // -100 (away dominant) .. +100 (home dominant), last 5 minutes
    double HomeControl,       // 0..100, last 5 minutes
    double AwayControl,
    double Chaos,             // 0..100, last 5 minutes
    double? HomePpda10,       // pressing intensity, last 10 minutes (lower = more intense)
    double? AwayPpda10,
    double TempoPassesPerMin, // both teams, last 5 minutes
    double TurnoversPerMin);

public sealed record PlayerSnapshot(
    string Id, string Name, int Number, string Position, Side Side, bool OnPitch,
    double Minutes, int Touches, int Passes, int PassesCompleted, double? PassAccuracyPct,
    double? AvgPassDifficulty, int KeyPasses, int Shots, int Goals, double Xg, int Tackles,
    int Interceptions, int Pressures, int Sprints, double TopSpeedKmh, double DistanceKm,
    double MaxShotSpeedKmh);

public sealed record MatchSnapshot(
    string MatchId, int Period, double Clock, int Minute, int HomeScore, int AwayScore,
    TeamSnapshot Home, TeamSnapshot Away, LiveSnapshot Live, IReadOnlyList<PlayerSnapshot> Players);

/// <summary>
/// Incremental match state. Feed events in order with <see cref="Apply"/>; read the current
/// picture with <see cref="Snapshot"/>. Every number the agents see comes from here.
/// </summary>
public sealed class MatchState
{
    private const double MaxAttributedGap = 6.0;   // dead-ball time is not possession
    private const double ShortWindow = 300;
    private const double PressWindow = 600;

    private readonly MatchInfo _info;
    private readonly Dictionary<Side, TeamStats> _teams = new() { [Side.Home] = new(), [Side.Away] = new() };
    private readonly Dictionary<string, PlayerStats> _players = [];
    private readonly LinkedList<(MatchEvent Event, Side Possessor, double Dt, double? Difficulty)> _recent = new();

    private Side _possessor = Side.Home;
    private MatchEvent? _last;
    private MatchEvent? _lastCompletedPass;

    public MatchState(MatchInfo info)
    {
        _info = info;
        foreach (var side in new[] { Side.Home, Side.Away })
            foreach (var p in info.Team(side).Starters)
                _players[p.Id] = new PlayerStats(p, side, 0);
    }

    public MatchInfo Info => _info;
    public double Clock { get; private set; }
    public double T { get; private set; }
    public int Period { get; private set; } = 1;
    public int HomeScore => _teams[Side.Home].Goals;
    public int AwayScore => _teams[Side.Away].Goals;
    public TeamStats Team(Side side) => _teams[side];
    public PlayerStats? Player(string? id) => id is not null && _players.TryGetValue(id, out var p) ? p : null;
    public Side Possessor => _possessor;

    public EventMetrics Apply(MatchEvent e)
    {
        // Attribute the time since the last event to whoever had the ball.
        var dt = _last is null ? 0 : Math.Min(Math.Max(e.T - _last.T, 0), MaxAttributedGap);
        _teams[_possessor].PossessionSeconds += dt;
        (Clock, T, Period) = (e.Clock, e.T, e.Period);
        TrackPpda(e);

        var team = _teams[e.Team];
        var player = Player(e.PlayerId);
        var metrics = new EventMetrics(e);

        switch (e.Type)
        {
            case EventType.Kickoff:
            case EventType.PossessionChange:
                if (e.Team != _possessor || e.Type == EventType.Kickoff) team.Possessions++;
                _possessor = e.Team;
                _lastCompletedPass = null;
                break;

            case EventType.Pass:
                _possessor = e.Team;
                var (sx, sy) = Pitch.AttackingFrame(e.Team, e.X!.Value, e.Y!.Value);
                var (ex, ey) = Pitch.AttackingFrame(e.Team, e.EndX!.Value, e.EndY!.Value);
                // An intercepted pass's end is where it was cut out; rate the intended pass length.
                var intendedEx = e.Outcome == "intercepted" && e.DistanceM is { } dm
                    ? sx + (ex - sx) * dm / Math.Max(Pitch.Distance(sx, sy, ex, ey), 0.1) : ex;
                var intendedEy = e.Outcome == "intercepted" && e.DistanceM is { } dm2
                    ? sy + (ey - sy) * dm2 / Math.Max(Pitch.Distance(sx, sy, ex, ey), 0.1) : ey;
                var difficulty = Pitch.PassDifficulty(sx, sy, intendedEx, intendedEy, e.UnderPressure);
                var completed = e.Outcome == "complete";
                var progressive = completed && Pitch.IsProgressive(sx, ex);
                team.Passes++;
                team.DifficultySum += difficulty;
                team.PassDistance += e.DistanceM ?? 0;
                if (completed) team.PassesCompleted++;
                if (progressive) team.ProgressivePasses++;
                if (player is not null)
                {
                    player.Touches++;
                    player.Passes++;
                    player.DifficultySum += difficulty;
                    if (completed) player.PassesCompleted++;
                }
                _lastCompletedPass = completed ? e : null;
                metrics = metrics with { PassDifficulty = Math.Round(difficulty, 3), Progressive = progressive };
                _recent.AddLast((e, _possessor, dt, difficulty));
                Trim();
                _last = e;
                return metrics;

            case EventType.Carry:
                _possessor = e.Team;
                if (player is not null) player.Touches++;
                if (e.Outcome != "complete") _lastCompletedPass = null;
                break;

            case EventType.Shot:
                _possessor = e.Team;
                team.Shots++;
                team.Xg += e.Xg ?? 0;
                if (e.Outcome is "goal" or "saved") team.ShotsOnTarget++;
                if (e.Outcome == "goal") team.Goals++;
                if (player is not null)
                {
                    player.Touches++;
                    player.Shots++;
                    player.Xg += e.Xg ?? 0;
                    player.MaxShotSpeedKmh = Math.Max(player.MaxShotSpeedKmh, e.BallSpeedKmh ?? 0);
                    if (e.Outcome == "goal") player.Goals++;
                }
                if (_lastCompletedPass is { } kp && kp.ReceiverId == e.PlayerId && Player(kp.PlayerId) is { } passer)
                {
                    passer.KeyPasses++;
                    metrics = metrics with { KeyPassCredit = true, KeyPasserId = passer.Player.Id };
                }
                _lastCompletedPass = null;
                break;

            case EventType.Tackle:
                team.Tackles++;
                if (player is not null) player.Tackles++;
                break;

            case EventType.Interception:
                team.Interceptions++;
                if (player is not null) player.Interceptions++;
                break;

            case EventType.Pressure:
                team.Pressures++;
                if (player is not null) player.Pressures++;
                break;

            case EventType.Foul:
                team.Fouls++;
                break;

            case EventType.Sprint:
                if (player is not null)
                {
                    player.Sprints++;
                    player.SprintDistance += e.DistanceM ?? 0;
                    player.TopSpeedKmh = Math.Max(player.TopSpeedKmh, e.PlayerSpeedKmh ?? 0);
                }
                break;

            case EventType.Substitution:
                if (Player(e.SubOffId) is { } off) off.OffClock = e.Clock;
                var on = _info.Team(e.Team).Player(e.PlayerId!);
                _players[on.Id] = new PlayerStats(on, e.Team, e.Clock);
                break;
        }

        _recent.AddLast((e, _possessor, dt, null));
        Trim();
        _last = e;
        return metrics;
    }

    private void Trim()
    {
        while (_recent.First is { } first && T - first.Value.Event.T > PressWindow)
            _recent.RemoveFirst();
    }

    public MatchSnapshot Snapshot() => new(
        _info.MatchId, Period, Clock, (int)(Clock / 60) + 1, HomeScore, AwayScore,
        TeamSnapshot(Side.Home), TeamSnapshot(Side.Away), Live(),
        _players.Values.Select(PlayerSnapshot).ToList());

    private TeamSnapshot TeamSnapshot(Side side)
    {
        var s = _teams[side];
        var totalPoss = _teams[Side.Home].PossessionSeconds + _teams[Side.Away].PossessionSeconds;
        return new TeamSnapshot(
            PossessionPct: totalPoss > 0 ? Math.Round(100 * s.PossessionSeconds / totalPoss, 1) : 50,
            Passes: s.Passes,
            PassesCompleted: s.PassesCompleted,
            PassAccuracyPct: s.Passes > 0 ? Math.Round(100.0 * s.PassesCompleted / s.Passes, 1) : 0,
            AvgPassDistanceM: s.Passes > 0 ? Math.Round(s.PassDistance / s.Passes, 1) : 0,
            AvgPassDifficulty: s.Passes > 0 ? Math.Round(s.DifficultySum / s.Passes, 3) : 0,
            ProgressivePasses: s.ProgressivePasses,
            Shots: s.Shots,
            ShotsOnTarget: s.ShotsOnTarget,
            Goals: s.Goals,
            Xg: Math.Round(s.Xg, 2),
            Tackles: s.Tackles,
            Interceptions: s.Interceptions,
            Pressures: s.Pressures,
            Fouls: s.Fouls,
            Ppda: FullMatchPpda(side));
    }

    private PlayerSnapshot PlayerSnapshot(PlayerStats p)
    {
        var minutes = Math.Max(0, ((p.OffClock ?? Clock) - p.OnClock) / 60);
        // Baseline running from minutes played, plus the sprints tracking picked up.
        var metresPerMinute = p.Player.Position == "GK" ? 45 : 108;
        var distanceKm = (minutes * metresPerMinute + p.SprintDistance) / 1000;
        return new PlayerSnapshot(
            p.Player.Id, p.Player.Name, p.Player.Number, p.Player.Position, p.Side, p.OffClock is null,
            Math.Round(minutes, 1), p.Touches, p.Passes, p.PassesCompleted,
            p.Passes > 0 ? Math.Round(100.0 * p.PassesCompleted / p.Passes, 1) : null,
            p.Passes > 0 ? Math.Round(p.DifficultySum / p.Passes, 3) : null,
            p.KeyPasses, p.Shots, p.Goals, Math.Round(p.Xg, 2), p.Tackles, p.Interceptions,
            p.Pressures, p.Sprints, p.TopSpeedKmh, Math.Round(distanceKm, 2), p.MaxShotSpeedKmh);
    }

    /// <summary>
    /// Passes allowed per defensive action: opponent passes in their own 60% of the pitch
    /// divided by this side's tackles, interceptions, fouls and loose-ball recoveries in that
    /// same zone. Lower means a more intense press.
    /// </summary>
    public double? Ppda(Side side, double window)
    {
        int oppPasses = 0, actions = 0;
        foreach (var (e, _, _, _) in Window(window))
        {
            var (p, a) = PpdaContribution(side, e);
            oppPasses += p;
            actions += a;
        }
        return PpdaRatio(oppPasses, actions);
    }

    public double? FullMatchPpda(Side side) => PpdaRatio(_ppdaTotals[side].OppPasses, _ppdaTotals[side].Actions);

    private static double? PpdaRatio(int oppPasses, int actions) =>
        actions == 0 ? null : Math.Round((double)oppPasses / actions, 1);

    private static (int OppPasses, int Actions) PpdaContribution(Side side, MatchEvent e)
    {
        if (e.X is null) return (0, 0);
        if (e.Type == EventType.Pass && e.Team != side)
            return (Pitch.AttackingFrame(e.Team, e.X.Value, e.Y!.Value).X <= 63 ? 1 : 0, 0);
        var defensiveAction = e.Type is EventType.Tackle or EventType.Interception or EventType.Foul
                              || e is { Type: EventType.PossessionChange, Outcome: "recovery" };
        if (e.Team == side && defensiveAction)
            return (0, Pitch.AttackingFrame(side, e.X.Value, e.Y!.Value).X >= 42 ? 1 : 0);
        return (0, 0);
    }

    // The window is trimmed, so full-match PPDA is tracked incrementally.
    private readonly Dictionary<Side, (int OppPasses, int Actions)> _ppdaTotals =
        new() { [Side.Home] = (0, 0), [Side.Away] = (0, 0) };

    private void TrackPpda(MatchEvent e)
    {
        foreach (var side in new[] { Side.Home, Side.Away })
        {
            var (p, a) = PpdaContribution(side, e);
            var (tp, ta) = _ppdaTotals[side];
            _ppdaTotals[side] = (tp + p, ta + a);
        }
    }

    private IEnumerable<(MatchEvent Event, Side Possessor, double Dt, double? Difficulty)> Window(double seconds) =>
        _recent.Where(r => T - r.Event.T <= seconds);

    private LiveSnapshot Live()
    {
        var window = Window(ShortWindow).ToList();
        var minutes = Math.Max(Math.Min(ShortWindow, T) / 60, 1);
        var poss = new Dictionary<Side, double> { [Side.Home] = 0, [Side.Away] = 0 };
        var passes = new Dictionary<Side, int> { [Side.Home] = 0, [Side.Away] = 0 };
        var completed = new Dictionary<Side, int> { [Side.Home] = 0, [Side.Away] = 0 };
        var xg = new Dictionary<Side, double> { [Side.Home] = 0, [Side.Away] = 0 };
        var entries = new Dictionary<Side, int> { [Side.Home] = 0, [Side.Away] = 0 };
        var possessions = new Dictionary<Side, int> { [Side.Home] = 0, [Side.Away] = 0 };
        int turnovers = 0, onBall = 0, pressured = 0, fouls = 0;

        foreach (var (e, possessor, dt, _) in window)
        {
            poss[possessor] += dt;
            switch (e.Type)
            {
                case EventType.Pass:
                    passes[e.Team]++;
                    onBall++;
                    if (e.UnderPressure) pressured++;
                    if (e.Outcome == "complete")
                    {
                        completed[e.Team]++;
                        var sx = Pitch.AttackingFrame(e.Team, e.X!.Value, e.Y!.Value).X;
                        var ex = Pitch.AttackingFrame(e.Team, e.EndX!.Value, e.EndY!.Value).X;
                        if (sx < 70 && ex >= 70) entries[e.Team]++;
                    }
                    break;
                case EventType.Carry:
                    onBall++;
                    if (e.UnderPressure) pressured++;
                    break;
                case EventType.Shot:
                    xg[e.Team] += e.Xg ?? 0;
                    onBall++;
                    break;
                case EventType.PossessionChange:
                    possessions[e.Team]++;
                    if (e.Outcome is "interception" or "tackle" or "recovery" or "blocked_shot") turnovers++;
                    break;
                case EventType.Kickoff:
                    possessions[e.Team]++;
                    break;
                case EventType.Foul:
                    fouls++;
                    break;
            }
        }

        double Share(Dictionary<Side, double> d, Side s) => d[Side.Home] + d[Side.Away] > 0 ? d[s] / (d[Side.Home] + d[Side.Away]) : 0.5;
        double ShareI(Dictionary<Side, int> d, Side s) => d[Side.Home] + d[Side.Away] > 0 ? (double)d[s] / (d[Side.Home] + d[Side.Away]) : 0.5;

        double Pressure(Side s) => 0.4 * Share(xg, s) + 0.3 * ShareI(entries, s) + 0.3 * Share(poss, s);
        var momentum = Math.Round(100 * (Pressure(Side.Home) - Pressure(Side.Away)), 1);

        double Control(Side s)
        {
            var accuracy = passes[s] > 0 ? (double)completed[s] / passes[s] : 0;
            var passesPerPossession = possessions[s] > 0 ? (double)passes[s] / possessions[s] : passes[s];
            return Math.Round(100 * (0.5 * Share(poss, s) + 0.3 * accuracy + 0.2 * Pitch.Clamp(passesPerPossession / 8)), 1);
        }

        var turnoversPerMin = turnovers / minutes;
        var chaos = Math.Round(100 * (0.5 * Pitch.Clamp(turnoversPerMin / 2.5)
                                      + 0.3 * (onBall > 0 ? (double)pressured / onBall : 0) * 2
                                      + 0.2 * Pitch.Clamp(fouls / minutes / 0.5)), 1);

        return new LiveSnapshot(
            momentum, Control(Side.Home), Control(Side.Away), Math.Min(chaos, 100),
            Ppda(Side.Home, PressWindow), Ppda(Side.Away, PressWindow),
            Math.Round((passes[Side.Home] + passes[Side.Away]) / minutes, 1),
            Math.Round(turnoversPerMin, 2));
    }
}

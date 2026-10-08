namespace Studio.Engine;

/// <summary>
/// Seeded synthetic match simulator. Generates a full 90-minute match of football-realistic
/// events from a single integer seed; the same seed always produces the same match, so demos
/// and tests replay exactly.
///
/// The model is a possession chain: the ball carrier is pressed (or not), then shoots,
/// carries or passes. Outcome probabilities come from the same metrics the insight engine
/// computes (pass difficulty, xG), and team styles drift during the match (a pressing switch
/// after the hour, fatigue, chasing the game) so the data holds real stories for the agents
/// to find. All clubs and players are fictional.
/// </summary>
public sealed class MatchSimulator
{
    public const double HalfSeconds = 45 * 60;

    // Slot, base x, base y in the team's attacking frame (own goal at x = 0).
    private static readonly (string Pos, double X, double Y)[] Formation433 =
    [
        ("GK", 5, 34), ("RB", 30, 10), ("RCB", 20, 25), ("LCB", 20, 43), ("LB", 30, 58),
        ("CDM", 40, 34), ("RCM", 52, 22), ("LCM", 52, 46), ("RW", 76, 10), ("ST", 86, 34),
        ("LW", 76, 58),
    ];
    private const int StrikerSlot = 9;
    private static readonly string[] BenchPositions = ["GK", "CB", "CM", "W", "ST"];
    private static readonly HashSet<string> Attackers = ["RW", "ST", "LW", "RCM", "LCM"];

    private static readonly (string Name, string Short, string Colour)[] Clubs =
    [
        ("Northbridge Athletic", "NBA", "#d7263d"),
        ("Riverside Rovers", "RIV", "#1b998b"),
        ("Ashvale United", "ASH", "#3f88c5"),
        ("Harbour Town", "HBT", "#f49d37"),
        ("Eastmere FC", "EMR", "#9b5de5"),
        ("Cobalt Park", "CBP", "#00a6fb"),
        ("Thornfield Wanderers", "THW", "#e4572e"),
        ("Saltmarsh City", "SMC", "#76b041"),
    ];
    private static readonly string[] Venues =
        ["Northbridge Arena", "Riverside Park", "The Ashvale Ground", "Harbourside Stadium"];
    private static readonly string[] Weather =
        ["Clear, 14°C", "Light rain, 9°C", "Overcast, 11°C", "Cold and windy, 6°C"];
    private static readonly string[] FirstNames =
    [
        "Ade", "Bruno", "Callum", "Dario", "Elias", "Femi", "Gabriel", "Hugo", "Idris", "Jonas",
        "Kai", "Luca", "Mateo", "Nico", "Omar", "Pavel", "Quinn", "Rafael", "Sami", "Tomas",
        "Udo", "Viktor", "Wes", "Xavi", "Yusuf", "Zane", "Kwame", "Leon", "Malik", "Theo",
        "Ruben", "Jamal", "Arlo", "Emeka", "Felix", "Ilan",
    ];
    private static readonly string[] LastNames =
    [
        "Adeyemi", "Bakker", "Castell", "Duarte", "Eriksen", "Fofana", "Grieve", "Holm",
        "Ibarra", "Jansen", "Kovac", "Lindqvist", "Mensah", "Novak", "Okafor", "Petrov",
        "Quarry", "Rossi", "Sato", "Torres", "Ulloa", "Varga", "Whitlock", "Yilmaz",
        "Zielinski", "Asante", "Brennan", "Coleby", "Darrow", "Ellison", "Ferraz", "Hale",
        "Marsh", "Oduya", "Strand", "Vance",
    ];

    private sealed record Style(double Strength, double Press, double Directness);

    private sealed class Slot(string position, double baseX, double baseY, Player player)
    {
        public string Position { get; } = position;
        public double BaseX { get; } = baseX;
        public double BaseY { get; } = baseY;
        public Player Player { get; set; } = player;
    }

    private sealed class TeamState(Style style, List<Slot> slots, List<Player> bench, List<double> subMinutes)
    {
        public Style Style { get; } = style;
        public List<Slot> Slots { get; } = slots;
        public List<Player> Bench { get; } = bench;
        public List<double> SubMinutes { get; } = subMinutes;
    }

    private readonly Rng _rng;
    private readonly MatchInfo _info;
    private readonly Dictionary<Side, TeamState> _teams = [];
    private readonly Side _pressSwitchSide;
    private readonly double _pressSwitchMinute;
    private readonly List<MatchEvent> _events = [];
    private readonly Dictionary<Side, int> _score = new() { [Side.Home] = 0, [Side.Away] = 0 };

    private int _seq;
    private double _t;
    private double _clock;
    private int _period = 1;
    private int _possession;
    private Side _side = Side.Home;
    private int _carrier = StrikerSlot;
    // Ball location in the possessing side's attacking frame.
    private double _bx = 52.5, _by = 34.0;

    public MatchSimulator(int seed)
    {
        _rng = new Rng(seed);
        _info = BuildMatchInfo(seed, _rng);
        foreach (var side in new[] { Side.Home, Side.Away })
        {
            var team = _info.Team(side);
            var style = new Style(_rng.Uniform(0.4, 0.75), _rng.Uniform(0.25, 0.55), _rng.Uniform(0.3, 0.8));
            var slots = Formation433.Select((f, i) => new Slot(f.Pos, f.X, f.Y, team.Starters[i])).ToList();
            var subs = Enumerable.Range(0, 3).Select(_ => _rng.Uniform(56, 86)).Order().ToList();
            _teams[side] = new TeamState(style, slots, [.. team.Bench], subs);
        }
        // A scripted tactical change for the engine to discover from the data alone.
        _pressSwitchSide = _rng.Chance(0.5) ? Side.Home : Side.Away;
        _pressSwitchMinute = _rng.Uniform(52, 66);
    }

    public static Match Simulate(int seed) => new MatchSimulator(seed).Run();

    public Match Run()
    {
        var first = _rng.Chance(0.5) ? Side.Home : Side.Away;
        PlayPeriod(1, first);
        PlayPeriod(2, first.Opponent());
        return new Match(_info, _events);
    }

    // ---------------------------------------------------------------- setup

    private static MatchInfo BuildMatchInfo(int seed, Rng rng)
    {
        var clubs = rng.Sample(Clubs, 2);
        var used = new HashSet<string>();
        return new MatchInfo(
            MatchId: $"SYN-{seed:D5}",
            Seed: seed,
            Competition: "Synthetic League",
            Venue: rng.Choice(Venues),
            Weather: rng.Choice(Weather),
            Home: BuildTeam(Side.Home, clubs[0], rng, used),
            Away: BuildTeam(Side.Away, clubs[1], rng, used));
    }

    private static Team BuildTeam(Side side, (string Name, string Short, string Colour) club, Rng rng,
        HashSet<string> used)
    {
        var prefix = side == Side.Home ? "H" : "A";
        string NewName()
        {
            while (true)
            {
                var n = $"{rng.Choice(FirstNames)} {rng.Choice(LastNames)}";
                if (used.Add(n)) return n;
            }
        }

        var numbers = rng.Sample(Enumerable.Range(2, 38).ToList(), 16);
        var starters = Formation433
            .Select((f, i) => new Player($"{prefix}{i + 1:D2}", NewName(), f.Pos == "GK" ? 1 : numbers[i], f.Pos))
            .ToList();
        var bench = BenchPositions
            .Select((pos, i) => new Player($"{prefix}{12 + i:D2}", NewName(), pos == "GK" ? 13 : numbers[11 + i], pos))
            .ToList();
        return new Team(side, club.Name, club.Short, club.Colour, "4-3-3", starters, bench);
    }

    // ---------------------------------------------------------------- helpers

    private Style CurrentStyle(Side side)
    {
        var b = _teams[side].Style;
        var minute = _clock / 60;
        var press = b.Press;
        var directness = b.Directness;
        if (side == _pressSwitchSide && minute >= _pressSwitchMinute) press += 0.22;
        if (_score[side] < _score[side.Opponent()] && minute > 65)
        {
            directness += 0.15;
            press += 0.08;
        }
        var fatigue = 0.06 * (minute / 90);
        return new Style(b.Strength - fatigue, Math.Min(press, 0.95), Math.Min(directness, 1.0));
    }

    private void Tick(double seconds)
    {
        _t += seconds;
        _clock += seconds;
    }

    private static double R1(double v) => Math.Round(v, 1);

    /// <summary>Record an event. Locations are in <paramref name="frame"/>'s attacking frame (default: team).</summary>
    private MatchEvent Emit(EventType type, Side team, Player? player,
        (double X, double Y)? loc = null, (double X, double Y)? end = null, Side? frame = null,
        string? outcome = null, string? receiverId = null, bool underPressure = false,
        double? ballSpeed = null, double? playerSpeed = null, double? distance = null,
        string? bodyPart = null, double? xg = null, string? subOffId = null)
    {
        var f = frame ?? team;
        (double X, double Y)? l = loc is { } a ? Pitch.FixedFrame(f, a.X, a.Y) : null;
        (double X, double Y)? e = end is { } b ? Pitch.FixedFrame(f, b.X, b.Y) : null;
        _seq++;
        var ev = new MatchEvent
        {
            Id = $"{_info.MatchId}-{_seq:D5}",
            Seq = _seq,
            T = R1(_t),
            Clock = R1(_clock),
            Period = _period,
            Type = type,
            Team = team,
            Possession = _possession,
            PlayerId = player?.Id,
            X = l is { } lx ? R1(lx.X) : null,
            Y = l is { } ly ? R1(ly.Y) : null,
            EndX = e is { } ex ? R1(ex.X) : null,
            EndY = e is { } ey ? R1(ey.Y) : null,
            Outcome = outcome,
            ReceiverId = receiverId,
            UnderPressure = underPressure,
            BallSpeedKmh = ballSpeed is { } bs ? R1(bs) : null,
            PlayerSpeedKmh = playerSpeed is { } ps ? R1(ps) : null,
            DistanceM = distance is { } d ? R1(d) : null,
            BodyPart = bodyPart,
            Xg = xg is { } x ? Math.Round(x, 3) : null,
            SubOffId = subOffId,
            Players = TrackedTypes.Contains(type) ? Track(player?.Id, l) : null,
        };
        _events.Add(ev);
        return ev;
    }

    private static readonly HashSet<EventType> TrackedTypes =
    [
        EventType.Kickoff, EventType.Pass, EventType.Carry, EventType.Shot,
        EventType.Tackle, EventType.Interception, EventType.PossessionChange, EventType.Foul,
    ];

    /// <summary>
    /// A tracking frame: every player's position in the fixed frame, from the same shape model the
    /// simulator uses to pick passes and tacklers. The player making the event stands where it happens.
    /// </summary>
    private List<PlayerPosition> Track(string? actorId, (double X, double Y)? at)
    {
        var frame = new List<PlayerPosition>(22);
        foreach (var side in new[] { Side.Home, Side.Away })
        {
            var slots = _teams[side].Slots;
            var positions = Positions(side);
            for (var i = 0; i < slots.Count; i++)
            {
                var id = slots[i].Player.Id;
                var (x, y) = Pitch.FixedFrame(_side, positions[i].X, positions[i].Y);
                if (side == _side && i == _carrier) (x, y) = Pitch.FixedFrame(_side, _bx, _by);
                if (id == actorId && at is { } p) (x, y) = p;
                frame.Add(new PlayerPosition(id, R1(x), R1(y)));
            }
        }
        return frame;
    }

    /// <summary>Approximate positions of a side's players, in the possessing side's frame.</summary>
    private List<(double X, double Y)> Positions(Side side)
    {
        var slots = _teams[side].Slots;
        var result = new List<(double X, double Y)>(slots.Count);
        if (side == _side)
        {
            var shift = (_bx - 45) * 0.45;
            foreach (var s in slots)
            {
                var gk = s.Position == "GK";
                var x = Pitch.Clamp(s.BaseX + (gk ? shift * 0.3 : shift), 3, gk ? 18 : 102);
                var y = Pitch.Clamp(s.BaseY + (_by - 34) * 0.25, 2, 66);
                result.Add((x, y));
            }
            return result;
        }
        // Defending block: compress towards own goal, then mirror into the possessor's frame.
        var ballOwn = Pitch.Length - _bx;
        foreach (var s in slots)
        {
            var x = s.Position == "GK" ? 4 : Pitch.Clamp(s.BaseX * 0.62 + (ballOwn - 40) * 0.3, 3, 70);
            var y = Pitch.Clamp(s.BaseY + ((Pitch.Width - _by) - 34) * 0.4, 2, 66);
            result.Add((Pitch.Length - x, Pitch.Width - y));
        }
        return result;
    }

    private int Nearest(Side side, double x, double y)
    {
        var pos = Positions(side);
        var best = 0;
        for (var i = 1; i < pos.Count; i++)
            if (Pitch.Distance(x, y, pos[i].X, pos[i].Y) < Pitch.Distance(x, y, pos[best].X, pos[best].Y))
                best = i;
        return best;
    }

    private Player PlayerAt(Side side, int slot) => _teams[side].Slots[slot].Player;

    /// <summary>Hand the ball to <paramref name="newSide"/> at (x, y) in the old possessor's frame.</summary>
    private void ChangePossession(Side newSide, double x, double y, int slot, string reason, double deadTime)
    {
        Restart(newSide, Pitch.Clamp(Pitch.Length - x, 1, 104), Pitch.Clamp(Pitch.Width - y, 1, 67), slot,
            reason, deadTime);
    }

    /// <summary>Give <paramref name="side"/> the ball at (x, y) in its own attacking frame.</summary>
    private void Restart(Side side, double x, double y, int slot, string reason, double deadTime)
    {
        _possession++;
        (_side, _carrier, _bx, _by) = (side, slot, x, y);
        Emit(EventType.PossessionChange, side, PlayerAt(side, slot), loc: (x, y), outcome: reason);
        Tick(deadTime);
    }

    // ---------------------------------------------------------------- match flow

    private void PlayPeriod(int period, Side kickoffSide)
    {
        _period = period;
        _clock = period == 1 ? 0 : HalfSeconds;
        var end = _clock + HalfSeconds + _rng.Uniform(60, period == 2 ? 300 : 180);
        Kickoff(kickoffSide);
        while (_clock < end)
        {
            MaybeSubstitute();
            Step();
        }
        Emit(EventType.PeriodEnd, Side.Home, null, outcome: $"end_of_period_{period}");
        Tick(1);
    }

    private void Kickoff(Side side)
    {
        _possession++;
        (_side, _carrier, _bx, _by) = (side, StrikerSlot, 52.5, 34.0);
        Emit(EventType.Kickoff, side, PlayerAt(side, StrikerSlot), loc: (52.5, 34.0));
        Tick(1);
    }

    private void MaybeSubstitute()
    {
        if (_period != 2) return;
        var minute = _clock / 60;
        foreach (var (side, ts) in _teams)
        {
            while (ts.SubMinutes.Count > 0 && minute >= ts.SubMinutes[0])
            {
                ts.SubMinutes.RemoveAt(0);
                var outfield = Enumerable.Range(0, ts.Slots.Count)
                    .Where(i => ts.Slots[i].Position != "GK" && !(side == _side && i == _carrier))
                    .ToList();
                var on = ts.Bench.FirstOrDefault(p => p.Position != "GK");
                if (on is null) break;
                var slot = _rng.Choice(outfield);
                ts.Bench.Remove(on);
                var off = ts.Slots[slot].Player;
                ts.Slots[slot].Player = on;
                Emit(EventType.Substitution, side, on, outcome: "substitution", subOffId: off.Id);
            }
        }
    }

    private void Step()
    {
        var opp = _side.Opponent();
        var ostyle = CurrentStyle(opp);

        var zone = 0.25 + 0.45 * Pitch.Clamp((60 - _bx) / 60) + (_bx > 80 ? 0.2 : 0);
        var underPressure = _rng.Chance(ostyle.Press * zone);
        if (underPressure)
        {
            var presser = Nearest(opp, _bx, _by);
            Emit(EventType.Pressure, opp, PlayerAt(opp, presser), loc: (_bx, _by), frame: _side);
        }

        var xg = Pitch.ShotXg(_bx, _by, underPressure, header: false);
        // Long-range efforts are rare but real; close chances get taken, and a crowded box
        // means many attacks die before a shot (handled in Pass/Carry).
        var pShot = _bx < 72 ? 0 : Pitch.Clamp(0.055 + xg * 0.35, 0, 0.3);
        const double pCarry = 0.16;
        var r = _rng.Next();
        if (r < pShot) Shot(underPressure);
        else if (r < pShot + pCarry) Carry(underPressure);
        else Pass(underPressure);

        if (_rng.Chance(0.03)) Sprint();
    }

    private void Pass(bool underPressure)
    {
        var side = _side;
        var opp = side.Opponent();
        var style = CurrentStyle(side);
        var ostyle = CurrentStyle(opp);
        var positions = Positions(side);
        var passer = PlayerAt(side, _carrier);

        var weights = new List<double>(positions.Count);
        for (var i = 0; i < positions.Count; i++)
        {
            if (i == _carrier) { weights.Add(0); continue; }
            var (px, py) = positions[i];
            var d = Pitch.Distance(_bx, _by, px, py);
            var w = Math.Exp(-Math.Pow((d - 17) / 14, 2)) * Math.Exp(style.Directness * (px - _bx) / 30);
            if (_teams[side].Slots[i].Position == "GK" && _bx > 30) w *= 0.03;
            weights.Add(w * _rng.Uniform(0.5, 1.5));
        }
        var receiver = _rng.Weighted(weights);
        var (rx, ry) = positions[receiver];
        var ex = Pitch.Clamp(rx + _rng.Gauss(0, 3), 1, 104);
        var ey = Pitch.Clamp(ry + _rng.Gauss(0, 3), 1, 67);
        var receiverId = PlayerAt(side, receiver).Id;

        var diff = Pitch.PassDifficulty(_bx, _by, ex, ey, underPressure);
        var crowding = Pitch.InBox(ex, ey) ? 0.22 : ex > 80 ? 0.08 : 0;
        var pComplete = Pitch.Clamp(
            1.02 - 0.55 * diff - crowding + 0.2 * (style.Strength - 0.5) - 0.12 * (ostyle.Press - 0.4), 0.2, 0.99);
        var dist = Pitch.Distance(_bx, _by, ex, ey);
        var speed = Pitch.Clamp(28 + dist * 1.3 + _rng.Gauss(0, 5), 18, 110);
        var travel = dist / (speed / 3.6);
        var body = _rng.Chance(0.04) ? "head" : _rng.Chance(0.3) ? "left_foot" : "right_foot";

        if (_rng.Chance(pComplete))
        {
            Emit(EventType.Pass, side, passer, loc: (_bx, _by), end: (ex, ey), outcome: "complete",
                receiverId: receiverId, underPressure: underPressure, ballSpeed: speed, distance: dist, bodyPart: body);
            Tick(travel + _rng.Uniform(1.0, 3.0));
            (_carrier, _bx, _by) = (receiver, ex, ey);
            return;
        }

        var r = _rng.Next();
        if (r < 0.2)
        {
            // Cut out somewhere along the line.
            var f = _rng.Uniform(0.45, 0.95);
            var ix = _bx + (ex - _bx) * f;
            var iy = _by + (ey - _by) * f;
            Emit(EventType.Pass, side, passer, loc: (_bx, _by), end: (ix, iy), outcome: "intercepted",
                receiverId: receiverId, underPressure: underPressure, ballSpeed: speed, distance: dist, bodyPart: body);
            Tick(travel * f);
            var interceptor = Nearest(opp, ix, iy);
            Emit(EventType.Interception, opp, PlayerAt(opp, interceptor), loc: (ix, iy), frame: side, outcome: "won");
            ChangePossession(opp, ix, iy, interceptor, "interception", _rng.Uniform(0.5, 1.5));
            return;
        }
        if (r < 0.72)
        {
            // Overhit or miscontrolled: a loose ball the opponent recovers near the target.
            var lx = Pitch.Clamp(ex + _rng.Gauss(0, 4), 1, 104);
            var ly = Pitch.Clamp(ey + _rng.Gauss(0, 4), 1, 67);
            Emit(EventType.Pass, side, passer, loc: (_bx, _by), end: (lx, ly), outcome: "incomplete",
                receiverId: receiverId, underPressure: underPressure, ballSpeed: speed, distance: dist, bodyPart: body);
            Tick(travel + _rng.Uniform(1.0, 2.5));
            ChangePossession(opp, lx, ly, Nearest(opp, lx, ly), "recovery", 0);
            return;
        }

        var ox = Pitch.Clamp(ex + _rng.Gauss(0, 6), 0, 105);
        var oy = ey < 34 ? 0.0 : 68.0;
        Emit(EventType.Pass, side, passer, loc: (_bx, _by), end: (ox, oy), outcome: "out",
            receiverId: receiverId, underPressure: underPressure, ballSpeed: speed, distance: dist, bodyPart: body);
        Tick(travel);
        var taker = Nearest(opp, ox, oy);
        ChangePossession(opp, ox, Pitch.Clamp(oy, 1, 67), taker, "out_of_play", _rng.Uniform(8, 20));
    }

    private void Carry(bool underPressure)
    {
        var side = _side;
        var opp = side.Opponent();
        var ostyle = CurrentStyle(opp);
        var carrier = PlayerAt(side, _carrier);
        var dist = _rng.Uniform(4, 16);
        var angle = _rng.Gauss(0, 0.6);
        var ex = Pitch.Clamp(_bx + dist * Math.Cos(angle), 1, 104);
        var ey = Pitch.Clamp(_by + dist * Math.Sin(angle), 1, 67);
        var pLost = 0.08 + (underPressure ? 0.18 : 0) + 0.1 * (ostyle.Press - 0.4) + (ex > 85 ? 0.15 : 0);
        var speed = _rng.Uniform(14, 26);

        if (!_rng.Chance(pLost))
        {
            Emit(EventType.Carry, side, carrier, loc: (_bx, _by), end: (ex, ey), outcome: "complete",
                underPressure: underPressure, distance: dist, playerSpeed: speed);
            Tick(dist / (speed / 3.6));
            (_bx, _by) = (ex, ey);
            return;
        }

        var hx = (_bx + ex) / 2;
        var hy = (_by + ey) / 2;
        Emit(EventType.Carry, side, carrier, loc: (_bx, _by), end: (hx, hy), outcome: "dispossessed",
            underPressure: underPressure, distance: dist / 2, playerSpeed: speed);
        Tick(dist / 2 / (speed / 3.6));
        var tackler = Nearest(opp, hx, hy);
        if (_rng.Chance(0.5))
        {
            // Fouled: the attacking side keeps the ball with a free kick.
            Emit(EventType.Foul, opp, PlayerAt(opp, tackler), loc: (hx, hy), frame: side, outcome: "free_kick");
            (_bx, _by) = (hx, hy);
            _possession++;
            Tick(_rng.Uniform(15, 35));
            return;
        }
        Emit(EventType.Tackle, opp, PlayerAt(opp, tackler), loc: (hx, hy), frame: side, outcome: "won");
        ChangePossession(opp, hx, hy, tackler, "tackle", _rng.Uniform(0.5, 1.5));
    }

    private void Shot(bool underPressure)
    {
        var side = _side;
        var opp = side.Opponent();
        var style = CurrentStyle(side);
        var shooter = PlayerAt(side, _carrier);
        var header = _bx > 94 && _rng.Chance(0.18);
        var xg = Pitch.ShotXg(_bx, _by, underPressure, header);
        var d = Pitch.Distance(_bx, _by, Pitch.Length, Pitch.GoalY);
        var speed = header ? _rng.Uniform(40, 72) : Pitch.Clamp(70 + d * 0.8 + _rng.Gauss(0, 9), 45, 125);
        var pGoal = Pitch.Clamp(xg * (0.8 + 0.4 * style.Strength));

        string outcome;
        if (_rng.Chance(pGoal)) outcome = "goal";
        else
        {
            var rest = _rng.Next();
            var blocked = underPressure ? 0.35 : 0.22;
            outcome = rest < blocked ? "blocked" : rest < blocked + 0.38 ? "saved" : "off_target";
        }
        var gy = outcome == "off_target"
            ? Pitch.GoalY + (_rng.Chance(0.5) ? -1 : 1) * _rng.Uniform(4.2, 9)
            : Pitch.GoalY + _rng.Uniform(-3.4, 3.4);
        (double, double) end = outcome == "blocked" ? (_bx + 2, _by) : (Pitch.Length, gy);
        Emit(EventType.Shot, side, shooter, loc: (_bx, _by), end: end, outcome: outcome,
            underPressure: underPressure, ballSpeed: speed, distance: d, bodyPart: header ? "head" : "foot", xg: xg);
        Tick(d / (speed / 3.6) + 0.5);

        switch (outcome)
        {
            case "goal":
                _score[side]++;
                Tick(_rng.Uniform(50, 75));
                Kickoff(opp);
                break;
            case "saved":
                Restart(opp, 6, 34, 0, "goalkeeper_save", _rng.Uniform(4, 9));
                break;
            case "off_target":
                Restart(opp, 6, 34, 0, "goal_kick", _rng.Uniform(15, 30));
                break;
            default:
                if (_rng.Chance(0.5))
                {
                    _bx = Math.Max(_bx - _rng.Uniform(3, 10), 60);
                    Tick(1);
                }
                else
                {
                    ChangePossession(opp, _bx, _by, Nearest(opp, _bx, _by), "blocked_shot", 1);
                }
                break;
        }
    }

    private void Sprint()
    {
        var side = _rng.Chance(0.65) ? _side : _side.Opponent();
        var slots = _teams[side].Slots;
        var outfield = Enumerable.Range(0, slots.Count).Where(i => slots[i].Position != "GK").ToList();
        var attackers = outfield.Where(i => Attackers.Contains(slots[i].Position)).ToList();
        var slot = _rng.Choice(attackers.Count > 0 && _rng.Chance(0.6) ? attackers : outfield);
        var (x, y) = Positions(side)[slot];
        var speed = Pitch.Clamp(_rng.Gauss(29.5, 2.6), 24, 36.8);
        Emit(EventType.Sprint, side, slots[slot].Player, loc: (x, y), frame: _side,
            playerSpeed: speed, distance: _rng.Uniform(15, 50));
    }
}

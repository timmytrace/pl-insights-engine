namespace Studio.Engine;

// Coordinates are metres on a 105 x 68 pitch, stored in a fixed frame where the home team
// always attacks towards x = 105 (no half-time switch), so any event can be plotted without
// knowing the period. Use Pitch.AttackingFrame to view a location from the acting team's side.

public enum Side { Home, Away }

public static class SideExtensions
{
    public static Side Opponent(this Side side) => side == Side.Home ? Side.Away : Side.Home;
}

public enum EventType
{
    Kickoff,
    Pass,
    Carry,
    Shot,
    Tackle,
    Interception,
    Pressure,
    Foul,
    PossessionChange,
    Sprint,
    Substitution,
    PeriodEnd,
}

public sealed record Player(string Id, string Name, int Number, string Position);

public sealed record Team(
    Side Side,
    string Name,
    string ShortName,
    string Colour,
    string Formation,
    IReadOnlyList<Player> Starters,
    IReadOnlyList<Player> Bench)
{
    public Player Player(string id) =>
        Starters.Concat(Bench).FirstOrDefault(p => p.Id == id)
        ?? throw new KeyNotFoundException(id);
}

public sealed record MatchInfo(
    string MatchId,
    int Seed,
    string Competition,
    string Venue,
    string Weather,
    Team Home,
    Team Away)
{
    public Team Team(Side side) => side == Side.Home ? Home : Away;
}

public sealed record MatchEvent
{
    public required string Id { get; init; }
    public required int Seq { get; init; }
    /// <summary>Elapsed seconds of play, monotonic across both halves.</summary>
    public required double T { get; init; }
    /// <summary>Match clock in seconds; the second half starts at 2700.</summary>
    public required double Clock { get; init; }
    public required int Period { get; init; }
    public required EventType Type { get; init; }
    public required Side Team { get; init; }
    /// <summary>Possession sequence number.</summary>
    public required int Possession { get; init; }
    public string? PlayerId { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? EndX { get; init; }
    public double? EndY { get; init; }
    public string? Outcome { get; init; }
    public string? ReceiverId { get; init; }
    public bool UnderPressure { get; init; }
    // Measured quantities, emitted the way a tracking system would supply them.
    public double? BallSpeedKmh { get; init; }
    public double? PlayerSpeedKmh { get; init; }
    public double? DistanceM { get; init; }
    public string? BodyPart { get; init; }
    public double? Xg { get; init; }
    public string? SubOffId { get; init; }
    /// <summary>Where all 22 players are at this event, like a tracking feed. On-ball events only.</summary>
    public IReadOnlyList<PlayerPosition>? Players { get; init; }

    public int Minute => (int)(Clock / 60) + 1;
}

public sealed record PlayerPosition(string Id, double X, double Y);

public sealed record Match(MatchInfo Info, IReadOnlyList<MatchEvent> Events);

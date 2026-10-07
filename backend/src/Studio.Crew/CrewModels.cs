using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Studio.Crew;

public enum CrewRole { Stats, Gaffer, Ref, Gallery, Host }

public enum CrewMessageKind
{
    Brief,      // Stats hands over the fact sheet
    ToolCall,   // an agent asked Stats for more numbers
    Pitch,      // The Gaffer proposes a story
    Verdict,    // Ref approves or rejects
    Decision,   // Gallery routes, airs or drops
    OnAir,      // The Host puts the card on screen
}

/// <summary>One line in the Control Room transcript.</summary>
public sealed record CrewMessage(
    string MomentId,
    CrewRole From,
    CrewMessageKind Kind,
    string Text,
    double MatchT,
    IReadOnlyDictionary<string, object>? Data = null);

/// <summary>A number or name on the fact sheet. Numbers are the only ones a card may quote.</summary>
public sealed record Fact(string Key, string Label, object Value, string? Unit = null)
{
    public double? Number => Value switch
    {
        double d => d,
        int i => i,
        long l => l,
        _ => null,
    };

    /// <summary>Counts must be quoted exactly; measurements may be rounded.</summary>
    public bool IsCount { get; init; }
}

/// <summary>Everything the crew is allowed to say about a moment, captured when it happened.</summary>
public sealed class FactSheet(string momentId, string kind, int minute, double matchT)
{
    private readonly Dictionary<string, Fact> _facts = [];
    private readonly Lock _gate = new();

    public string MomentId { get; } = momentId;
    public string Kind { get; } = kind;
    public int Minute { get; } = minute;
    public double MatchT { get; } = matchT;

    public IReadOnlyList<Fact> Facts
    {
        get { lock (_gate) return _facts.Values.ToList(); }
    }

    public void Add(string key, string label, object value, string? unit = null, bool isCount = false)
    {
        if (value is double d) value = Math.Round(d, 3);
        lock (_gate) _facts[key] = new Fact(key, label, value, unit) { IsCount = isCount };
    }

    public bool Has(string key)
    {
        lock (_gate) return _facts.ContainsKey(key);
    }

    /// <summary>The sheet as the agents see it: a flat JSON object of key → value.</summary>
    public string ToPromptJson() => JsonSerializer.Serialize(new
    {
        moment = new { id = MomentId, kind = Kind, minute = Minute },
        facts = Facts.ToDictionary(f => f.Key, f => f.Value),
        labels = Facts.ToDictionary(f => f.Key, f => f.Unit is null ? f.Label : $"{f.Label} ({f.Unit})"),
    });

    public static string Format(double v) =>
        Math.Abs(v - Math.Round(v)) < 1e-9 ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
        : v.ToString(Math.Abs(v) < 1 ? "0.00" : "0.0", CultureInfo.InvariantCulture);
}

public sealed record Claim(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("facts")] IReadOnlyList<string> Facts);

/// <summary>The Gaffer's proposed story for a moment.</summary>
public sealed record StoryPitch(
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("claims")] IReadOnlyList<Claim> Claims);

public sealed record Verdict(bool Approved, IReadOnlyList<string> Reasons, int ClaimsChecked, int NumbersChecked);

/// <summary>Ref's second opinion from the model, for qualitative overreach the number check can't see.</summary>
public sealed record ReviewDto(
    [property: JsonPropertyName("approved")] bool Approved,
    [property: JsonPropertyName("reasons")] IReadOnlyList<string>? Reasons);

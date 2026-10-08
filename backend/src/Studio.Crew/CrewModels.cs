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

    /// <summary>Facts about the moment itself, which the story should lead with; the rest is context.</summary>
    public bool AboutMoment { get; init; }

    /// <summary>Running match totals (possession, pass accuracy...), which mean little in the first minutes.</summary>
    public bool MatchTotal { get; init; }
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

    public void Add(string key, string label, object value, string? unit = null, bool isCount = false, bool aboutMoment = false,
        bool matchTotal = false)
    {
        if (value is double d) value = Math.Round(d, 3);
        lock (_gate) _facts[key] = new Fact(key, label, value, unit) { IsCount = isCount, AboutMoment = aboutMoment, MatchTotal = matchTotal };
    }

    public Fact? Get(string key)
    {
        lock (_gate) return _facts.GetValueOrDefault(key);
    }

    /// <summary>
    /// The fact a citation refers to. Models cite keys ("distanceM"), keys with a group in front
    /// ("about_this_moment.xg") or the label ("Distance", "ball speed kmh"); all point at the same fact.
    /// </summary>
    public Fact? Resolve(string cited)
    {
        var key = cited.Split('.').Last().Trim();
        if (Get(key) is { } exact) return exact;
        var wanted = Normalise(key);
        lock (_gate)
            return _facts.Values.FirstOrDefault(f => Normalise(f.Key) == wanted)
                   ?? _facts.Values.FirstOrDefault(f => Normalise(f.Label) == wanted);
    }

    private static string Normalise(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public bool Has(string key)
    {
        lock (_gate) return _facts.ContainsKey(key);
    }

    /// <summary>
    /// The sheet as the agents see it. Facts about the moment come first and are kept apart from
    /// match context, so the story leads with what just happened. Each entry is [value, label].
    /// </summary>
    public string ToPromptJson()
    {
        object Entry(Fact f) => new[] { f.Value, f.Unit is null ? f.Label : $"{f.Label} ({f.Unit})" };
        var facts = Facts;
        return JsonSerializer.Serialize(new
        {
            moment = new { id = MomentId, kind = Kind, minute = Minute },
            about_this_moment = facts.Where(f => f.AboutMoment).ToDictionary(f => f.Key, Entry),
            context = facts.Where(f => !f.AboutMoment).ToDictionary(f => f.Key, Entry),
        });
    }

    /// <summary>The sheet as plain lines ("Label: value unit"), e.g. as grounding context for an evaluator.</summary>
    public string ToReadableText() =>
        string.Join(Environment.NewLine, Facts.Select(f => $"{f.Label}: {f.Value}{(f.Unit is null ? "" : " " + f.Unit)}"));

    /// <summary>Read the sheet back out of a prompt (used by the offline model): key → value across both groups.</summary>
    public static Dictionary<string, JsonElement> ReadPromptFacts(JsonElement root)
    {
        var all = new Dictionary<string, JsonElement>();
        foreach (var group in new[] { "about_this_moment", "context" })
            if (root.TryGetProperty(group, out var g))
                foreach (var p in g.EnumerateObject()) all[p.Name] = p.Value[0];
        return all;
    }

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

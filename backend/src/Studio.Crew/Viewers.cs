using Studio.Engine;

namespace Studio.Crew;

public enum Persona { Analyst, Casual, ClubFan, PlayerFocus, MetricFocus }

/// <summary>
/// Who a pane is for. Written compactly so it fits in a URL:
/// <c>analyst.en</c>, <c>casual.es</c>, <c>club.fr.home</c>, <c>player.en.H10</c>, <c>metric.es.pressing</c>.
/// </summary>
public sealed record ViewerProfile(string Id, Persona Persona, string Language, Side? Club = null, string? PlayerId = null,
    string? Metric = null)
{
    public static readonly string[] Languages = ["en", "es", "fr"];

    /// <summary>The single performance metrics a viewer can choose to follow all match.</summary>
    public static readonly string[] Metrics = ["xg", "passing", "pressing", "speed"];

    public static ViewerProfile? Parse(string spec)
    {
        var parts = spec.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !Languages.Contains(parts[1])) return null;
        var lang = parts[1];
        return parts[0] switch
        {
            "analyst" => new(spec, Persona.Analyst, lang),
            "casual" => new(spec, Persona.Casual, lang),
            "club" when parts.Length == 3 && Enum.TryParse<Side>(parts[2], true, out var side) => new(spec, Persona.ClubFan, lang, side),
            "player" when parts.Length == 3 => new(spec, Persona.PlayerFocus, lang, PlayerId: parts[2]),
            "metric" when parts.Length == 3 && Metrics.Contains(parts[2]) => new(spec, Persona.MetricFocus, lang, Metric: parts[2]),
            _ => null,
        };
    }

    public static IReadOnlyList<ViewerProfile> ParseList(string? specs, int max = 4) =>
        (specs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Parse).OfType<ViewerProfile>().DistinctBy(v => v.Id).Take(max).ToList();
}

/// <summary>
/// The Host's editorial rules: personalisation is also about what a viewer does NOT see.
/// Plain code, so every choice is predictable and explainable.
/// </summary>
public static class Relevance
{
    private static readonly HashSet<MomentKind> CasualKinds =
        [MomentKind.Goal, MomentKind.BigChance, MomentKind.MomentumSwing, MomentKind.ShotSpeed, MomentKind.SprintSpeed];

    public static bool Shows(ViewerProfile v, Moment m) => v.Persona switch
    {
        Persona.Analyst => true,
        Persona.Casual => CasualKinds.Contains(m.Kind) || IsGoalMilestone(m),
        Persona.ClubFan => m.Team == v.Club || m.Kind is MomentKind.Goal or MomentKind.BigChance or MomentKind.MomentumSwing,
        Persona.PlayerFocus => m.PlayerId == v.PlayerId || m.Kind == MomentKind.Goal,
        Persona.MetricFocus => m.Kind == MomentKind.Goal || MetricKinds.GetValueOrDefault(v.Metric ?? "", []).Contains(m.Kind),
        _ => true,
    };

    // The moments that move each metric.
    private static readonly Dictionary<string, HashSet<MomentKind>> MetricKinds = new()
    {
        ["xg"] = [MomentKind.BigChance, MomentKind.MomentumSwing],
        ["passing"] = [MomentKind.ElitePass, MomentKind.ControlSpell, MomentKind.Milestone],
        ["pressing"] = [MomentKind.PressSurge, MomentKind.ChaosSpell, MomentKind.ControlSpell],
        ["speed"] = [MomentKind.SprintSpeed, MomentKind.ShotSpeed],
    };

    private static bool IsGoalMilestone(Moment m) =>
        m.Kind == MomentKind.Milestone && m.Facts.TryGetValue("milestone", out var x) && x is "brace" or "hat_trick";
}

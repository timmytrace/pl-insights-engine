using Studio.Crew;
using Studio.Engine;

namespace Studio.Api;

/// <summary>
/// A match's graphics as a timeline a broadcast graphics engine can load directly: every item
/// says when it goes on and off (match clock), where it sits, how important it is, and what it
/// shows, already laid out in named layers. Described by docs/overlay-timeline.schema.json.
/// </summary>
public static class OverlayTimeline
{
    public const string Version = "1.0";
    public const string SchemaUrl = "https://github.com/timmytrace/pl-insights-engine/blob/main/docs/overlay-timeline.schema.json";

    public sealed record Layers(string Headline, string Body, IReadOnlyList<CardStat> Stats);

    public sealed record Item(
        string Id, string MomentId, string Kind, string Slot, int Priority,
        double In, double Out, string InLabel, string Team, string? PlayerId,
        Layers Layers, string Language, string Persona, IReadOnlyList<string> Evidence);

    public sealed record Timeline(
        string Schema, string Version, string MatchId, string Timebase, string? Viewer,
        IReadOnlyList<string> Slots, IReadOnlyList<Item> Items);

    public static Timeline Build(MatchSummary summary, ViewerProfile? viewer)
    {
        var moments = summary.Moments.ToDictionary(m => m.Id);
        var cards = summary.Cards
            .Where(c => viewer is null || Relevance.Shows(viewer, moments[c.MomentId]))
            .Select(c => viewer is null ? c : TemplateLocalizer.ForViewer(c, viewer, summary.Info));

        var items = cards.Select(c => new Item(
            c.Id, c.MomentId, Snake(c.Kind.ToString()), c.Slot, c.Priority,
            In: Math.Round(c.Clock, 1), Out: Math.Round(c.Clock + c.DurationS, 1), InLabel: $"{c.Minute}'",
            Team: c.Team.ToString().ToLowerInvariant(), c.PlayerId,
            new Layers(c.Headline, c.Body, c.Stats), c.Language, c.Persona, c.Evidence)).ToList();

        return new Timeline(SchemaUrl, Version, summary.Info.MatchId, "match_clock_seconds", viewer?.Id,
            ["banner", "lower_third", "corner", "player_tag"], items);
    }

    private static string Snake(string pascal) =>
        string.Concat(pascal.Select((ch, i) => i > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
}

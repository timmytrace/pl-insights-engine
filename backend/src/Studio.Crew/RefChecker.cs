using System.Globalization;
using System.Text.RegularExpressions;

namespace Studio.Crew;

/// <summary>
/// Ref's rulebook: the deterministic half of fact-checking. Every number in a pitch must match a
/// fact on the sheet, every claim must cite facts that exist, and claims may not reach beyond
/// what the sheet can support (season records, certainty). The model only gets a say after
/// these checks pass.
/// </summary>
public static partial class RefChecker
{
    // Phrases the sheet can never back up: we only have this match's data.
    private static readonly (string Phrase, string Reason)[] Overreach =
    [
        ("season", "We only have this match's data, so nothing can be said about the season."),
        ("all year", "We only have this match's data, so nothing can be said about the year."),
        ("record", "Records need history the sheet doesn't have."),
        ("ever", "'Ever' needs history the sheet doesn't have."),
        ("best in the league", "League comparisons need data the sheet doesn't have."),
        ("always", "'Always' is a certainty the data can't support."),
        ("never", "'Never' is a certainty the data can't support."),
        ("guaranteed", "Nothing in football is guaranteed."),
        ("definitely", "'Definitely' is a certainty the data can't support."),
        ("unstoppable", "'Unstoppable' is opinion, not evidence."),
    ];

    // Numbers that are part of the language of the game rather than claims (scales, the 90 minutes).
    private static readonly HashSet<double> Neutral = [100, 90, 45];

    public static Verdict Check(StoryPitch pitch, FactSheet sheet)
    {
        var reasons = new List<string>();
        var facts = sheet.Facts;

        foreach (var claim in pitch.Claims)
        {
            if (claim.Facts.Count == 0)
                reasons.Add($"\"{claim.Text}\" doesn't cite any facts.");
            foreach (var key in claim.Facts.Where(k => !sheet.Has(k)))
                reasons.Add($"\"{claim.Text}\" cites '{key}', which isn't on the fact sheet.");
        }

        var text = string.Join(" ", new[] { pitch.Headline, pitch.Body }.Concat(pitch.Claims.Select(c => c.Text)));
        var numbersChecked = 0;
        foreach (Match m in NumberPattern().Matches(text))
        {
            numbersChecked++;
            var literal = m.Value.Replace(",", "");
            var n = double.Parse(literal, CultureInfo.InvariantCulture);
            if (Neutral.Contains(Math.Abs(n))) continue;
            if (!facts.Any(f => Supports(f, n, literal)))
                reasons.Add($"{m.Value} isn't on the fact sheet.");
        }

        var lower = text.ToLowerInvariant();
        foreach (var (phrase, reason) in Overreach)
            if (Regex.IsMatch(lower, $@"\b{Regex.Escape(phrase)}\b"))
                reasons.Add($"\"{phrase}\": {reason}");

        return new Verdict(reasons.Count == 0, reasons.Distinct().ToList(), pitch.Claims.Count, numbersChecked);
    }

    /// <summary>
    /// A quoted number matches a fact if it is the fact at the precision it was written
    /// (0.9 matches 0.91; 0.5 does not). Counts must match exactly. Fractions may be quoted as
    /// percentages or out of 100, and signed values (momentum) by their size.
    /// </summary>
    internal static bool Supports(Fact fact, double n, string literal)
    {
        if (fact.Number is not { } v) return false;
        var decimals = literal.Contains('.') ? literal.Length - literal.IndexOf('.') - 1 : 0;
        var unit = Math.Pow(10, -decimals);

        if (fact.IsCount) return Math.Abs(n - v) < 1e-9;

        var candidates = Math.Abs(v) <= 1 ? new[] { Math.Abs(v), Math.Abs(v) * 100 } : [Math.Abs(v)];
        return candidates.Any(c =>
            Math.Abs(n - c) <= unit * 0.5 + 1e-9                                    // rounded
            || decimals == 0 && Math.Abs(n - Math.Truncate(c)) < 1e-9);            // truncated
    }

    [GeneratedRegex(@"(?<![\w.])\d{1,3}(?:,\d{3})*(?:\.\d+)?|(?<![\w.])\d+(?:\.\d+)?")]
    private static partial Regex NumberPattern();
}

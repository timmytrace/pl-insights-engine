using System.Globalization;
using System.Text.RegularExpressions;

namespace Studio.Crew;

/// <summary>
/// Ref's rulebook: the deterministic half of fact-checking. Every number in a pitch must match a
/// fact on the sheet, every claim must cite facts that exist, and claims may not reach beyond
/// what the sheet can support (season records, certainty). The model only gets a say after
/// these checks pass. The same rules run on every translated version The Host writes.
/// </summary>
public static partial class RefChecker
{
    // Phrases the sheet can never back up: we only have this match's data.
    private static readonly Dictionary<string, (string Phrase, string Reason)[]> Overreach = new()
    {
        ["en"] =
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
        ],
        ["es"] =
        [
            ("temporada", "Season claim: we only have this match's data."),
            ("récord", "Records need history the sheet doesn't have."),
            ("siempre", "'Siempre' (always) is a certainty the data can't support."),
            ("nunca", "'Nunca' (never) is a certainty the data can't support."),
            ("imparable", "'Imparable' is opinion, not evidence."),
        ],
        ["fr"] =
        [
            ("saison", "Season claim: we only have this match's data."),
            ("record", "Records need history the sheet doesn't have."),
            ("toujours", "'Toujours' (always) is a certainty the data can't support."),
            ("jamais", "'Jamais' (never) is a certainty the data can't support."),
            ("imparable", "'Imparable' is opinion, not evidence."),
        ],
    };

    /// <summary>Before this minute, running match totals (possession, pass accuracy) can't be quoted.</summary>
    public const int EarlyMinutes = 15;

    // Numbers that are part of the language of the game rather than claims (scales, the 90 minutes).
    private static readonly HashSet<double> Neutral = [100, 90, 45];

    public static Verdict Check(StoryPitch pitch, FactSheet sheet)
    {
        var reasons = new List<string>();
        foreach (var claim in pitch.Claims)
        {
            if (claim.Facts.Count == 0)
                reasons.Add($"\"{claim.Text}\" doesn't cite any facts.");
            foreach (var key in claim.Facts.Where(k => sheet.Resolve(k) is null))
                reasons.Add($"\"{claim.Text}\" cites '{key}', which isn't on the fact sheet.");
        }

        // Match-total percentages after a few minutes are noise, not insight.
        if (sheet.Minute < EarlyMinutes)
            foreach (var f in pitch.Claims.SelectMany(c => c.Facts).Select(sheet.Resolve).OfType<Fact>().Distinct())
                if (f.MatchTotal)
                    reasons.Add($"It's minute {sheet.Minute}: {f.Label.ToLowerInvariant()} for the match doesn't mean anything yet.");

        // Internal fact names are for the crew, never for the screen.
        var onScreen = $"{pitch.Headline} {pitch.Body}";
        foreach (var f in sheet.Facts.Where(f => f.Key.Contains('_') || f.Key.Any(char.IsUpper)))
            if (Regex.IsMatch(onScreen, $@"\b{Regex.Escape(f.Key)}\b"))
                reasons.Add($"'{f.Key}' is an internal fact name and can't appear on screen.");

        var text = string.Join(" ", new[] { pitch.Headline, pitch.Body }.Concat(pitch.Claims.Select(c => c.Text)));
        var numbers = CheckText(text, sheet, "en", reasons);
        return new Verdict(reasons.Count == 0, reasons.Distinct().ToList(), pitch.Claims.Count, numbers);
    }

    /// <summary>Models sometimes cite a fact with its group in front ("about_this_moment.xg"); the key is the last part.</summary>
    internal static string Key(string cited) => cited.Split('.').Last().Trim();

    /// <summary>Check a finished piece of copy in any supported language. Returns how many numbers were checked.</summary>
    public static int CheckText(string text, FactSheet sheet, string language, List<string> reasons)
    {
        // Spanish and French write 0,89 for 0.89; our numbers never need thousands separators.
        if (language != "en") text = DecimalComma().Replace(text, "$1.$2");

        var facts = sheet.Facts;
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
        foreach (var lang in new[] { "en", language }.Distinct())
            foreach (var (phrase, reason) in Overreach.GetValueOrDefault(lang, []))
                if (Regex.IsMatch(lower, $@"\b{Regex.Escape(phrase)}\b"))
                    reasons.Add($"\"{phrase}\": {reason}");
        return numbersChecked;
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

    [GeneratedRegex(@"(\d),(\d)")]
    private static partial Regex DecimalComma();
}

using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Studio.Engine;

namespace Studio.Crew;

public sealed record RecapLine(
    [property: JsonPropertyName("speaker")] string Speaker,
    [property: JsonPropertyName("text")] string Text);

public sealed record RecapDto([property: JsonPropertyName("lines")] IReadOnlyList<RecapLine>? Lines);

/// <summary>A full-time recap as a short studio conversation, checked by Ref before anyone says it.</summary>
public sealed record RecapScript(
    string MatchId,
    string Language,
    IReadOnlyList<RecapLine> Lines,
    bool Verified,
    IReadOnlyList<string> Reasons,
    int NumbersChecked,
    int Attempts);

/// <summary>
/// Writes the full-time recap. Stats builds a full-time fact sheet (score, team numbers, the key
/// moments, the standout players); The Host writes a short conversation between the crew in the
/// viewer's language; Ref checks every number in every line and sends it back if anything fails.
/// </summary>
public sealed class RecapWriter(IChatClient chat, int maxAttempts = 3)
{
    public static readonly string[] Speakers = ["host", "gaffer", "stats", "ref"];

    private readonly AIAgent _agent = StudioCrew.Agent(chat, CrewPrompts.Recap, "The Host", "Presents the full-time recap with the crew");

    public static FactSheet FullTimeSheet(MatchState state, IReadOnlyList<Moment> moments)
    {
        var info = state.Info;
        var snap = state.Snapshot();
        var sheet = new FactSheet($"{info.MatchId}-FT", "FullTime", snap.Minute, state.T);
        sheet.Add("home_team", "Home team", info.Home.Name, aboutMoment: true);
        sheet.Add("away_team", "Away team", info.Away.Name, aboutMoment: true);
        sheet.Add("home_score", $"{info.Home.Name} goals", snap.HomeScore, isCount: true, aboutMoment: true);
        sheet.Add("away_score", $"{info.Away.Name} goals", snap.AwayScore, isCount: true, aboutMoment: true);
        sheet.Add("venue", "Venue", info.Venue);

        foreach (var (prefix, t) in new[] { ("home", snap.Home), ("away", snap.Away) })
        {
            sheet.Add($"{prefix}_possession", "Possession", t.PossessionPct, "%");
            sheet.Add($"{prefix}_xg", "Expected goals", t.Xg);
            sheet.Add($"{prefix}_shots", "Shots", t.Shots, isCount: true);
            sheet.Add($"{prefix}_shots_on_target", "Shots on target", t.ShotsOnTarget, isCount: true);
            sheet.Add($"{prefix}_passes", "Passes", t.Passes, isCount: true);
            sheet.Add($"{prefix}_pass_accuracy", "Pass accuracy", t.PassAccuracyPct, "%");
            if (t.Ppda is { } ppda) sheet.Add($"{prefix}_ppda", "PPDA, full match (lower = more intense press)", ppda);
        }

        // The key moments, most important first, so the recap tells the story of the match.
        var key = moments
            .Where(m => m.Kind is MomentKind.Goal or MomentKind.BigChance or MomentKind.PressSurge or MomentKind.MomentumSwing)
            .OrderByDescending(m => m.Kind == MomentKind.Goal).ThenByDescending(m => m.Severity).ThenBy(m => m.T)
            .Take(8).OrderBy(m => m.T).ToList();
        for (var i = 0; i < key.Count; i++)
        {
            var m = key[i];
            var who = m.PlayerName is { } p ? $"{p}, {info.Team(m.Team).Name}" : info.Team(m.Team).Name;
            sheet.Add($"moment_{i + 1}", $"Key moment {i + 1}", $"{Describe(m.Kind)} ({who})", aboutMoment: true);
            sheet.Add($"moment_{i + 1}_minute", $"Key moment {i + 1} minute", m.Minute, isCount: true, aboutMoment: true);
            if (m.Facts.TryGetValue("xg", out var xg) && xg is double x) sheet.Add($"moment_{i + 1}_xg", $"Key moment {i + 1} xG", x);
        }

        var standouts = snap.Players
            .OrderByDescending(p => p.Goals * 10 + p.KeyPasses * 3 + p.Xg * 5 + p.PassesCompleted / 20.0)
            .Take(3).ToList();
        for (var i = 0; i < standouts.Count; i++)
        {
            var p = standouts[i];
            var prefix = $"star_{i + 1}";
            sheet.Add($"{prefix}_name", "Standout player", $"{p.Name} ({info.Team(p.Side).Name})");
            sheet.Add($"{prefix}_goals", "Goals", p.Goals, isCount: true);
            sheet.Add($"{prefix}_key_passes", "Key passes", p.KeyPasses, isCount: true);
            sheet.Add($"{prefix}_passes_completed", "Passes completed", p.PassesCompleted, isCount: true);
            sheet.Add($"{prefix}_distance_km", "Distance covered", p.DistanceKm, "km");
        }
        return sheet;
    }

    public async Task<RecapScript> WriteAsync(FactSheet sheet, string matchId, string language, CancellationToken ct)
    {
        var session = await _agent.CreateSessionAsync(ct);
        var options = new ChatClientAgentRunOptions(new ChatOptions { ResponseFormat = ChatResponseFormat.Json, MaxOutputTokens = 900, Temperature = 0.6f });
        var request = CrewPrompts.RecapRequest(sheet, language);
        IReadOnlyList<RecapLine> lines = [];
        var reasons = new List<string>();
        var numbers = 0;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var response = await _agent.RunAsync(request, session, options, ct);
            lines = (StudioCrew.Parse<RecapDto>(response.Text)?.Lines ?? [])
                .Where(l => !string.IsNullOrWhiteSpace(l.Text))
                .Select(l => l with { Speaker = Speakers.Contains(l.Speaker?.ToLowerInvariant()) ? l.Speaker!.ToLowerInvariant() : "host" })
                .ToList();
            reasons.Clear();
            if (lines.Count == 0) reasons.Add("The recap wasn't valid JSON with lines.");
            numbers = RefChecker.CheckText(string.Join(" ", lines.Select(l => l.Text)), sheet, language, reasons);
            if (reasons.Count == 0) return new RecapScript(matchId, language, lines, true, [], numbers, attempt);
            request = CrewPrompts.RevisionRequest(sheet, reasons.Distinct().ToList());
        }
        return new RecapScript(matchId, language, lines, false, reasons.Distinct().ToList(), numbers, maxAttempts);
    }

    private static string Describe(MomentKind kind) => kind switch
    {
        MomentKind.Goal => "goal",
        MomentKind.BigChance => "big chance",
        MomentKind.PressSurge => "pressing surge",
        MomentKind.MomentumSwing => "momentum swing",
        _ => kind.ToString(),
    };
}

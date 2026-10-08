using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Studio.Crew;

/// <summary>
/// An offline stand-in for the model, so the whole crew runs without Azure. It reads the fact
/// sheet out of the prompt and writes in each character's voice. To show the review loop, The
/// Gaffer overreaches on roughly one moment in three (a season claim or a wrong number) and
/// backs down when Ref sends it back. Everything here is scripted; switch Crew:Mode to "azure"
/// for real model output.
/// </summary>
public sealed class ScriptedChatClient(int latencyMs) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var instructions = options?.Instructions + string.Join("\n", list.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
        var prompt = string.Join("\n", list.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        if (latencyMs > 0) await Task.Delay(latencyMs + Math.Abs(prompt.Length % 200), cancellationToken);

        var text = instructions.Contains(CrewPrompts.GafferTag) ? Gaffer(prompt)
            : instructions.Contains(CrewPrompts.RefTag) ? """{"approved":true,"reasons":[]}"""
            : instructions.Contains(CrewPrompts.HostTag) ? ScriptedHost.Write(prompt)
            : instructions.Contains(CrewPrompts.RecapTag) ? ScriptedRecap.Write(prompt)
            : "{}";
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { ModelId = "scripted-mock" };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates()) yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    // ------------------------------------------------------------------ The Gaffer

    private static string Gaffer(string prompt)
    {
        var start = prompt.IndexOf("<facts>", StringComparison.Ordinal) + 7;
        var end = prompt.IndexOf("</facts>", StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(prompt[start..end]);
        var moment = doc.RootElement.GetProperty("moment");
        var facts = FactSheet.ReadPromptFacts(doc.RootElement);
        var id = moment.GetProperty("id").GetString()!;
        var kind = moment.GetProperty("kind").GetString()!;
        var revising = prompt.Contains(CrewPrompts.RevisionMarker, StringComparison.Ordinal);

        string S(string key) => facts.TryGetValue(key, out var v)
            ? v.ValueKind == JsonValueKind.Number ? FactSheet.Format(v.GetDouble()) : v.ToString()
            : "";
        double N(string key) => facts.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        Claim C(string text, params string[] keys) => new(text, keys);

        var team = S("team");
        var opp = S("opponent");
        var player = S("player_name");
        var (headline, body, claims) = kind switch
        {
            "Goal" => ($"{player} makes it count",
                N("xg") >= 0.3
                    ? $"{team} turned pressure into a goal: a {S("xg")} xG chance, the kind that should go in."
                    : $"Only a {S("xg")} xG chance, so this one was about the finish, not the build-up.",
                new List<Claim>
                {
                    C($"The chance was worth {S("xg")} xG", "xg"),
                    C($"{team} have {S("team_xg")} xG to {S("opp_xg")} for {opp}", "team_xg", "opp_xg"),
                }),
            "BigChance" => ($"{team} should be ahead",
                $"A {S("xg")} xG chance ends {S("outcome").Replace('_', ' ')}. {team} are creating the better openings.",
                new List<Claim>
                {
                    C($"The chance was worth {S("xg")} xG", "xg"),
                    C($"{team} lead the xG {S("team_xg")} to {S("opp_xg")}", "team_xg", "opp_xg"),
                }),
            "PressSurge" => ($"{team} are hunting the ball",
                $"PPDA down to {S("ppda10")} over the last 10 minutes from {S("matchPpda")} across the match. {opp} get fewer passes before losing it.",
                new List<Claim>
                {
                    C($"{team} allow {S("ppda10")} passes per defensive action in the last 10 minutes", "ppda10"),
                    C($"Across the match it is {S("matchPpda")}", "matchPpda"),
                }),
            "MomentumSwing" => ($"The game has turned to {team}",
                $"Momentum went from {S("previousMomentum")} to {S("momentum")}: they're getting into the final third and shooting while {opp} chase.",
                new List<Claim>
                {
                    C($"Momentum moved from {S("previousMomentum")} to {S("momentum")}", "previousMomentum", "momentum"),
                }),
            "ChaosSpell" => ("Nobody can keep the ball",
                $"{S("turnoversPerMin")} turnovers a minute. This is a scrap, not a passing game: whoever settles first takes over.",
                new List<Claim>
                {
                    C($"{S("turnoversPerMin")} turnovers a minute", "turnoversPerMin"),
                    C($"Chaos index at {S("chaos")}", "chaos"),
                }),
            "ControlSpell" => ($"{team} have the game where they want it",
                $"Control index {S("control")} over the last 5 minutes. {opp} are chasing shadows.",
                new List<Claim>
                {
                    C($"Control index {S("control")} for {team}", "control"),
                    C($"{S("tempoPassesPerMin")} passes a minute", "tempoPassesPerMin"),
                }),
            "ElitePass" => ($"{player} picks the lock",
                $"Difficulty {Math.Round(N("difficulty") * 100):0}/100 over {S("distanceM")} m. Passes like that are what break a block.",
                new List<Claim>
                {
                    C($"Pass difficulty {Math.Round(N("difficulty") * 100):0} out of 100", "difficulty"),
                    C($"{S("distanceM")} m pass", "distanceM"),
                }),
            _ => ($"{team} moment", "", new List<Claim>()),
        };

        if (!revising && Stable(id) % 3 == 0)
        {
            if (Stable(id) % 2 == 0)
            {
                body += " Best spell of the season, that.";
            }
            else
            {
                var inflated = (N("team_shots") + 3).ToString(CultureInfo.InvariantCulture);
                body += $" That's {inflated} shots for {team} now.";
                claims.Add(C($"{team} have had {inflated} shots", "team_shots"));
            }
        }

        return JsonSerializer.Serialize(new StoryPitch(headline, body, claims));
    }

    private static int Stable(string s) => s.Aggregate(17, (h, c) => unchecked(h * 31 + c)) & int.MaxValue;
}

using System.ComponentModel;
using Microsoft.Extensions.AI;
using Studio.Engine;

namespace Studio.Crew;

/// <summary>
/// Stats, the Data Analyst. Pure code, no model: every number the crew can use comes from here.
/// The brief is built from a snapshot taken the instant the moment happened, so the crew can
/// keep working while the match moves on without its numbers drifting.
/// </summary>
public sealed record Briefing(FactSheet Sheet, MatchSnapshot Snapshot, MatchInfo Info, Moment Moment);

public sealed class StatsAnalyst
{
    public const int ShortWindowMinutes = 5;
    public const int PressWindowMinutes = 10;

    public Briefing Brief(Moment m, MatchState state)
    {
        var info = state.Info;
        var snap = state.Snapshot();
        var sheet = new FactSheet(m.Id, m.Kind.ToString(), m.Minute, m.T);
        var team = info.Team(m.Team);
        var opp = info.Team(m.Team.Opponent());
        var ts = m.Team == Side.Home ? snap.Home : snap.Away;
        var os = m.Team == Side.Home ? snap.Away : snap.Home;

        sheet.Add("minute", "Match minute", m.Minute, isCount: true);
        sheet.Add("team", "Team", team.Name);
        sheet.Add("opponent", "Opponent", opp.Name);
        sheet.Add("home_score", $"{info.Home.Name} goals", snap.HomeScore, isCount: true);
        sheet.Add("away_score", $"{info.Away.Name} goals", snap.AwayScore, isCount: true);
        sheet.Add("short_window", "Live window", ShortWindowMinutes, "min", isCount: true);
        sheet.Add("press_window", "Pressing window", PressWindowMinutes, "min", isCount: true);

        // Momentum is measured home +100 to away -100. Flip it so positive always means the
        // moment's team is on top; models misread the raw sign for away teams.
        var sign = m.Team == Side.Home ? 1 : -1;
        foreach (var (key, value) in m.Facts)
        {
            if (value is string s && s.Length == 0) continue;
            if (key is "momentum" or "previousMomentum" && value is double mo)
            {
                sheet.Add(key, key == "momentum" ? $"Momentum for {team.Name} now (+ means they're on top)" : $"Momentum for {team.Name} before the swing",
                    mo * sign, aboutMoment: true);
                continue;
            }
            if (key is "homeScore" or "awayScore") continue;   // already on the sheet as home_score / away_score
            sheet.Add(key, Humanise(key), value is bool b ? (b ? "yes" : "no") : value, aboutMoment: true);
        }

        AddTeam(sheet, "team", ts);
        AddTeam(sheet, "opp", os);

        var live = snap.Live;
        sheet.Add("team_momentum", $"Momentum for {team.Name}, last 5 min (+ means they're on top)", live.Momentum * sign);
        sheet.Add("team_control", "Team control index", m.Team == Side.Home ? live.HomeControl : live.AwayControl);
        sheet.Add("opp_control", "Opponent control index", m.Team == Side.Home ? live.AwayControl : live.HomeControl);
        sheet.Add("live_chaos", "Chaos index now", live.Chaos);
        sheet.Add("tempo", "Passes per minute, both teams", live.TempoPassesPerMin);
        sheet.Add("turnovers_per_min", "Turnovers per minute", live.TurnoversPerMin);
        if ((m.Team == Side.Home ? live.HomePpda10 : live.AwayPpda10) is { } tp) sheet.Add("team_ppda10", "Team PPDA, last 10 min (lower = more intense press)", tp);
        if ((m.Team == Side.Home ? live.AwayPpda10 : live.HomePpda10) is { } op) sheet.Add("opp_ppda10", "Opponent PPDA, last 10 min (lower = more intense press)", op);

        if (m.PlayerId is { } pid && snap.Players.FirstOrDefault(p => p.Id == pid) is { } player)
            AddPlayer(sheet, "player", player, aboutMoment: true);

        return new Briefing(sheet, snap, info, m);
    }

    /// <summary>
    /// Tools other agents can call for more context. Each result is written onto the sheet first,
    /// so Ref can check any number that came from a tool call.
    /// </summary>
    public IList<AITool> Tools(FactSheet sheet, MatchSnapshot snap, MatchInfo info, Action<string> onCall)
    {
        [Description("Get a player's match stats by name. Adds them to the fact sheet under the prefix 'p_<surname>'.")]
        string GetPlayerStats([Description("Player name as it appears on the fact sheet")] string name)
        {
            var p = snap.Players.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? snap.Players.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (p is null) return $"No player called {name}.";
            var prefix = "p_" + p.Name.Split(' ').Last().ToLowerInvariant();
            AddPlayer(sheet, prefix, p);
            onCall($"Player stats for {p.Name}");
            return string.Join("; ", sheet.Facts.Where(f => f.Key.StartsWith(prefix + "_")).Select(f => $"{f.Key}={f.Value}"));
        }

        [Description("Get the last 10 minutes of pressing for both teams (PPDA: lower means a more intense press).")]
        string GetPressing()
        {
            if (snap.Live.HomePpda10 is { } h) sheet.Add("home_ppda10", $"{info.Home.Name} PPDA, last 10 min", h);
            if (snap.Live.AwayPpda10 is { } a) sheet.Add("away_ppda10", $"{info.Away.Name} PPDA, last 10 min", a);
            onCall("Pressing, last 10 minutes");
            return $"home_ppda10={snap.Live.HomePpda10}; away_ppda10={snap.Live.AwayPpda10}";
        }

        return
        [
            AIFunctionFactory.Create(GetPlayerStats, "get_player_stats"),
            AIFunctionFactory.Create(GetPressing, "get_pressing"),
        ];
    }

    private static void AddTeam(FactSheet sheet, string prefix, TeamSnapshot t)
    {
        sheet.Add($"{prefix}_possession", "Possession", t.PossessionPct, "%", matchTotal: true);
        sheet.Add($"{prefix}_xg", "Expected goals", t.Xg);
        sheet.Add($"{prefix}_shots", "Shots", t.Shots, isCount: true);
        sheet.Add($"{prefix}_shots_on_target", "Shots on target", t.ShotsOnTarget, isCount: true);
        sheet.Add($"{prefix}_passes", "Passes", t.Passes, isCount: true);
        sheet.Add($"{prefix}_pass_accuracy", "Pass accuracy", t.PassAccuracyPct, "%", matchTotal: true);
        if (t.Ppda is { } ppda) sheet.Add($"{prefix}_ppda", "PPDA, full match (lower = more intense press)", ppda);
    }

    /// <summary>For player-focus viewers: the focused player's numbers, under the prefix "focus".</summary>
    public static void AddFocusPlayer(Briefing b, string playerId)
    {
        if (b.Snapshot.Players.FirstOrDefault(p => p.Id == playerId) is { } p) AddPlayer(b.Sheet, "focus", p);
    }

    private static void AddPlayer(FactSheet sheet, string prefix, PlayerSnapshot p, bool aboutMoment = false)
    {
        sheet.Add($"{prefix}_name", "Player", p.Name, aboutMoment: aboutMoment);
        sheet.Add($"{prefix}_position", "Position", p.Position, aboutMoment: aboutMoment);
        sheet.Add($"{prefix}_goals", "Goals", p.Goals, isCount: true, aboutMoment: aboutMoment);
        sheet.Add($"{prefix}_shots", "Shots", p.Shots, isCount: true, aboutMoment: aboutMoment);
        sheet.Add($"{prefix}_xg", "Expected goals", p.Xg, aboutMoment: aboutMoment);
        sheet.Add($"{prefix}_passes_completed", "Passes completed", p.PassesCompleted, isCount: true, aboutMoment: aboutMoment);
        if (p.PassAccuracyPct is { } acc) sheet.Add($"{prefix}_pass_accuracy", "Pass accuracy", acc, "%", aboutMoment: aboutMoment);
        sheet.Add($"{prefix}_key_passes", "Key passes", p.KeyPasses, isCount: true, aboutMoment: aboutMoment);
        sheet.Add($"{prefix}_distance_km", "Distance covered", p.DistanceKm, "km", aboutMoment: aboutMoment);
        if (p.TopSpeedKmh > 0) sheet.Add($"{prefix}_top_speed", "Top speed", p.TopSpeedKmh, "km/h", aboutMoment: aboutMoment);
    }

    private static string Humanise(string key) => key switch
    {
        "xg" => "Expected goals of this shot",
        "shotSpeedKmh" => "Shot speed",
        "distanceM" => "Distance",
        "ppda10" => "PPDA, last 10 min (lower = more intense press)",
        "matchPpda" => "PPDA, full match",
        "previousMomentum" => "Momentum before the swing",
        _ => System.Text.RegularExpressions.Regex.Replace(key, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant(),
    };
}

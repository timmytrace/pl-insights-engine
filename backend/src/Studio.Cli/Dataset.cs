using System.Globalization;
using System.Text;
using Studio.Engine;

/// <summary>
/// Writes the synthetic dataset: a one-row-per-match summary for many seeds, and full event
/// files (CSV, plus JSONL with tracking frames) for a few. Every file regenerates byte-for-byte
/// from its seed, so the committed sample is small and the rest can be produced on demand.
/// </summary>
internal static class Dataset
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Write(int[] summarySeeds, int[] eventSeeds, string outDir)
    {
        Directory.CreateDirectory(Path.Combine(outDir, "events"));

        var matches = new StringBuilder();
        matches.AppendLine("seed,match_id,home,away,home_goals,away_goals,home_xg,away_xg,home_shots,away_shots,home_shots_on_target,away_shots_on_target," +
                           "home_passes,away_passes,home_pass_accuracy,away_pass_accuracy,home_possession,away_possession,home_ppda,away_ppda,events,duration_min");
        foreach (var seed in summarySeeds)
        {
            var match = MatchSimulator.Simulate(seed);
            var state = new MatchState(match.Info);
            foreach (var e in match.Events) state.Apply(e);
            var s = state.Snapshot();
            matches.AppendLine(string.Join(",",
                seed, match.Info.MatchId, Csv(match.Info.Home.Name), Csv(match.Info.Away.Name), s.HomeScore, s.AwayScore,
                F(s.Home.Xg), F(s.Away.Xg), s.Home.Shots, s.Away.Shots, s.Home.ShotsOnTarget, s.Away.ShotsOnTarget,
                s.Home.Passes, s.Away.Passes, F(s.Home.PassAccuracyPct), F(s.Away.PassAccuracyPct),
                F(s.Home.PossessionPct), F(s.Away.PossessionPct), F(s.Home.Ppda), F(s.Away.Ppda),
                match.Events.Count, F(match.Events[^1].Clock / 60)));
        }
        File.WriteAllText(Path.Combine(outDir, "matches.csv"), matches.ToString().ReplaceLineEndings("\n"));

        foreach (var seed in eventSeeds)
        {
            var match = MatchSimulator.Simulate(seed);
            var csv = new StringBuilder();
            csv.AppendLine("id,seq,t,clock,period,minute,type,team,player_id,x,y,end_x,end_y,outcome,receiver_id,under_pressure," +
                           "possession,ball_speed_kmh,player_speed_kmh,distance_m,body_part,xg");
            foreach (var e in match.Events)
                csv.AppendLine(string.Join(",",
                    e.Id, e.Seq, F(e.T), F(e.Clock), e.Period, e.Minute, Snake(e.Type.ToString()), e.Team.ToString().ToLowerInvariant(),
                    e.PlayerId, F(e.X), F(e.Y), F(e.EndX), F(e.EndY), e.Outcome, e.ReceiverId, e.UnderPressure ? 1 : 0,
                    e.Possession, F(e.BallSpeedKmh), F(e.PlayerSpeedKmh), F(e.DistanceM), e.BodyPart, F(e.Xg)));
            File.WriteAllText(Path.Combine(outDir, "events", $"seed-{seed}.csv"), csv.ToString().ReplaceLineEndings("\n"));
        }

        Console.WriteLine($"Wrote {summarySeeds.Length} match summaries and {eventSeeds.Length} event files to {outDir}");
        return 0;
    }

    public static int[] ParseRange(string spec) =>
        spec.Split(',').SelectMany(part => part.Contains('-')
            ? Enumerable.Range(int.Parse(part.Split('-')[0]), int.Parse(part.Split('-')[1]) - int.Parse(part.Split('-')[0]) + 1)
            : [int.Parse(part)]).ToArray();

    private static string F(double? v) => v is { } x ? x.ToString("0.###", Inv) : "";

    private static string Csv(string s) => s.Contains(',') ? $"\"{s}\"" : s;

    private static string Snake(string pascal) =>
        string.Concat(pascal.Select((ch, i) => i > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
}

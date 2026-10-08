using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Studio.Crew;
using Studio.Engine;

/// <summary>
/// Measures the live crew on real simulated matches. Each story is scored by an LLM judge
/// (Microsoft.Extensions.AI.Evaluation, using the Foundry deployment) for groundedness against
/// the fact sheet, relevance, coherence and fluency, both as The Gaffer first drafted it and as
/// Ref finally approved it. Ref's own record (first-time approvals, why pitches were sent back,
/// time to air) comes from the crew run itself.
/// </summary>
internal static class Evaluation
{
    private sealed record Scores(double Groundedness, double Relevance, double Coherence, double Fluency);

    private sealed record Story(
        int Seed, int Minute, string Kind, int Drafts, bool FirstApproved, bool Aired, long MsToAir,
        IReadOnlyList<string> Rejections, Scores? FirstDraft, Scores? Final, Scores? SpanishCasual,
        string? FirstDraftText, string? FinalText);

    private static readonly string[] Metrics =
    [
        GroundednessEvaluator.GroundednessMetricName, RelevanceEvaluator.RelevanceMetricName,
        CoherenceEvaluator.CoherenceMetricName, FluencyEvaluator.FluencyMetricName,
    ];

    public static async Task<int> RunAsync(string endpoint, string deployment, int[] seeds, int perSeed, string outDir)
    {
        var options = new CrewOptions { Mode = "azure", Endpoint = endpoint, Deployment = deployment, MaxConcurrentMoments = 1000, MockLatencyMs = 0 };
        var chat = ModelFactory.Create(options);
        var judge = new ChatConfiguration(chat);
        IEvaluator evaluator = new CompositeEvaluator(new GroundednessEvaluator(), new RelevanceEvaluator(), new CoherenceEvaluator(), new FluencyEvaluator());
        var viewers = ViewerProfile.ParseList("casual.es");
        var stories = new List<Story>();

        foreach (var seed in seeds)
        {
            var match = MatchSimulator.Simulate(seed);
            var pipeline = new MatchPipeline(match.Info);
            var crew = new StudioCrew(chat, options);
            var count = 0;
            foreach (var e in match.Events)
            {
                if (count >= perSeed) break;
                var step = pipeline.Process(e);
                foreach (var m in step.Moments)
                {
                    if (count >= perSeed || crew.Gallery.Route(m).Route != CrewRoute.Crew) continue;
                    count++;
                    var briefing = crew.Brief(m, pipeline.State);
                    var template = step.Cards.First(c => c.MomentId == m.Id);
                    var clock = Stopwatch.StartNew();
                    long msToAir = -1;
                    var result = await crew.RunAsync(briefing, template, viewers, () => m.T,
                        msg => { if (msg.Kind == CrewMessageKind.OnAir) msToAir = clock.ElapsedMilliseconds; }, CancellationToken.None);

                    var request = $"In one on-screen broadcast graphic, explain why this {Describe(m.Kind)} at minute {m.Minute} matters to viewers.";
                    var grounding = new GroundednessEvaluatorContext(briefing.Sheet.ToReadableText());
                    async Task<Scores?> Score(string? text)
                    {
                        if (string.IsNullOrWhiteSpace(text)) return null;
                        var r = await evaluator.EvaluateAsync(request, text, judge, [grounding]);
                        double V(string name) => r.Get<NumericMetric>(name).Value ?? double.NaN;
                        return new Scores(V(Metrics[0]), V(Metrics[1]), V(Metrics[2]), V(Metrics[3]));
                    }

                    var firstText = result.Drafts.Count > 0 ? Text(result.Drafts[0].Pitch.Headline, result.Drafts[0].Pitch.Body) : null;
                    var finalText = result.Card is { } card ? Text(card.Headline, card.Body) : null;
                    var spanish = result.Versions.FirstOrDefault(v => v.Verified && v.Viewer.Language == "es")?.Card;
                    var story = new Story(seed, m.Minute, m.Kind.ToString(), result.Drafts.Count,
                        result.Drafts.Count > 0 && result.Drafts[0].Verdict.Approved, result.Card is not null, msToAir,
                        result.Drafts.Where(d => !d.Verdict.Approved).SelectMany(d => d.Verdict.Reasons).ToList(),
                        await Score(firstText), finalText == firstText ? null : await Score(finalText),
                        spanish is null ? null : await Score(Text(spanish.Headline, spanish.Body)), firstText, finalText);
                    // When the first draft aired unchanged its scores are the final scores too.
                    if (story.Final is null && story.Aired && story.FirstApproved) story = story with { Final = story.FirstDraft };
                    stories.Add(story);
                    Console.WriteLine($"seed {seed} {m.Minute,3}' {m.Kind,-14} drafts={story.Drafts} aired={story.Aired} " +
                                      $"ground {story.FirstDraft?.Groundedness:0.#}->{story.Final?.Groundedness:0.#} {msToAir,6}ms");
                }
            }
        }

        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "results.json"),
            JsonSerializer.Serialize(stories, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(outDir, "README.md"), Report(stories, deployment, seeds));
        Console.WriteLine($"Wrote {Path.Combine(outDir, "README.md")}");
        return 0;
    }

    private static string Text(string headline, string body) => $"{headline}. {body}";

    /// <summary>Rebuild the report from a saved results.json, without re-running the crew.</summary>
    public static async Task<int> ReportOnlyAsync(string outDir, string deployment, int[] seeds)
    {
        var stories = JsonSerializer.Deserialize<List<Story>>(await File.ReadAllTextAsync(Path.Combine(outDir, "results.json")))!;
        await File.WriteAllTextAsync(Path.Combine(outDir, "README.md"), Report(stories, deployment, seeds));
        Console.WriteLine($"Rewrote {Path.Combine(outDir, "README.md")} from {stories.Count} stories");
        return 0;
    }

    private static string Report(List<Story> stories, string deployment, int[] seeds)
    {
        // If Ref sent a draft back and then cleared the same text (the reviewer agent can vary between
        // calls), what aired is that draft: score it as the final.
        stories = stories.Select(s => s.Final is null && s.Aired && s.FinalText == s.FirstDraftText ? s with { Final = s.FirstDraft } : s).ToList();
        var inv = CultureInfo.InvariantCulture;
        string Mean(IEnumerable<double> xs) { var v = xs.Where(x => !double.IsNaN(x)).ToList(); return v.Count == 0 ? "–" : v.Average().ToString("0.00", inv); }
        string Pct(int n, int d) => d == 0 ? "–" : (100.0 * n / d).ToString("0", inv) + "%";
        string Row(string label, IEnumerable<Scores?> set)
        {
            var s = set.OfType<Scores>().ToList();
            return $"| {label} | {s.Count} | {Mean(s.Select(x => x.Groundedness))} | {Mean(s.Select(x => x.Relevance))} | {Mean(s.Select(x => x.Coherence))} | {Mean(s.Select(x => x.Fluency))} |";
        }
        static double Percentile(List<long> xs, double p) => xs.Count == 0 ? double.NaN : xs.OrderBy(x => x).ElementAt((int)Math.Min(xs.Count - 1, Math.Round(p * (xs.Count - 1))));

        var revised = stories.Where(s => s.Drafts > 1 && s.Final is not null).ToList();
        var aired = stories.Where(s => s.Aired).ToList();
        var times = aired.Select(s => s.MsToAir).Where(ms => ms >= 0).ToList();
        var reasons = stories.SelectMany(s => s.Rejections).GroupBy(Category).OrderByDescending(g => g.Count()).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Crew evaluation");
        sb.AppendLine();
        sb.AppendLine($"Generated {DateTime.UtcNow:yyyy-MM-dd} by `dotnet run --project src/Studio.Cli -c Release -- evaluate`, on {stories.Count} story moments from matches {string.Join(", ", seeds)}, with the live crew on Azure OpenAI (`{deployment}`).");
        sb.AppendLine();
        sb.AppendLine("## Quality, scored by an LLM judge (1–5)");
        sb.AppendLine();
        sb.AppendLine("Each story's on-screen text was scored with `Microsoft.Extensions.AI.Evaluation.Quality` (groundedness against the moment's fact sheet, relevance, coherence, fluency). \"First draft\" is The Gaffer's first pitch, before Ref; \"Final\" is what aired.");
        sb.AppendLine();
        sb.AppendLine("| Text | Stories | Groundedness | Relevance | Coherence | Fluency |");
        sb.AppendLine("|---|---|---|---|---|---|");
        sb.AppendLine(Row("The Gaffer's first draft", stories.Select(s => s.FirstDraft)));
        sb.AppendLine(Row("Final, on air", stories.Select(s => s.Final)));
        sb.AppendLine(Row("Sent back by Ref: first draft", revised.Select(s => s.FirstDraft)));
        sb.AppendLine(Row("Sent back by Ref: final", revised.Select(s => s.Final)));
        sb.AppendLine(Row("The Host, casual fan in Spanish", stories.Select(s => s.SpanishCasual)));
        sb.AppendLine();
        sb.AppendLine("## Ref's record");
        sb.AppendLine();
        sb.AppendLine("| Measure | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Approved first time | {Pct(stories.Count(s => s.FirstApproved), stories.Count)} |");
        sb.AppendLine($"| Sent back at least once | {Pct(stories.Count(s => s.Drafts > 1), stories.Count)} |");
        sb.AppendLine($"| Drafts per story (mean) | {stories.Average(s => s.Drafts).ToString("0.00", inv)} |");
        sb.AppendLine($"| Stories that aired | {Pct(aired.Count, stories.Count)} |");
        sb.AppendLine($"| Never cleared (template stayed) | {stories.Count(s => !s.Aired)} |");
        sb.AppendLine($"| Moment to verified card, median | {Percentile(times, 0.5) / 1000:0.0} s |");
        sb.AppendLine($"| Moment to verified card, 90th percentile | {Percentile(times, 0.9) / 1000:0.0} s |");
        sb.AppendLine();
        sb.AppendLine("### Why Ref sent pitches back");
        sb.AppendLine();
        sb.AppendLine("| Reason | Times |");
        sb.AppendLine("|---|---|");
        foreach (var g in reasons) sb.AppendLine($"| {g.Key} | {g.Count()} |");
        sb.AppendLine();
        sb.AppendLine("### Examples");
        sb.AppendLine();
        foreach (var s in revised.Take(3))
        {
            sb.AppendLine($"**{s.Minute}' {s.Kind}** (match {s.Seed})");
            sb.AppendLine();
            sb.AppendLine($"- First draft: {s.FirstDraftText}");
            sb.AppendLine($"- Ref: {string.Join(" ", s.Rejections.Take(2))}");
            sb.AppendLine($"- On air: {s.FinalText}");
            sb.AppendLine();
        }
        sb.AppendLine("## Caveats");
        sb.AppendLine();
        sb.AppendLine("- The judge is the same model family as the crew, which can flatter it; the before/after comparison is the meaningful signal, not the absolute scores.");
        sb.AppendLine("- Groundedness is judged against the fact sheet as plain text, the same facts the crew saw.");
        sb.AppendLine("- Raw per-story results are in [`results.json`](results.json).");
        return sb.ToString();
    }

    private static string Category(string reason) =>
        reason.Contains("doesn't mean anything yet") ? "Quoted a match total too early"
        : reason.Contains("isn't on the fact sheet") && reason.Contains("cites") ? "Cited a fact that doesn't exist"
        : reason.Contains("isn't on the fact sheet") ? "A number that isn't on the fact sheet"
        : reason.Contains("internal fact name") ? "Fact name left in the on-screen text"
        : reason.Contains("doesn't cite any facts") ? "Claim without a cited fact"
        : reason.Contains("valid JSON") ? "Unreadable pitch"
        : reason.StartsWith('"') && reason.Contains("\": ") ? "Season, record or certainty claim"
        : "Reviewer agent: claim doesn't follow from the facts";

    private static string Describe(MomentKind kind) => kind switch
    {
        MomentKind.BigChance => "big chance",
        MomentKind.PressSurge => "pressing surge",
        MomentKind.MomentumSwing => "momentum swing",
        MomentKind.ChaosSpell => "spell of chaos",
        MomentKind.ControlSpell => "spell of control",
        MomentKind.ElitePass => "standout pass",
        _ => kind.ToString().ToLowerInvariant(),
    };
}

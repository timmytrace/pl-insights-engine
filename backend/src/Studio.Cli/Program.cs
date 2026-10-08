using System.Text.Json;
using Studio.Crew;
using Studio.Engine;

// studio generate --seed 7 [--out data/sample/seed-7]   writes match.json (metadata) + events.jsonl
// studio summary  --seed 7                          prints full-time stats for a seed
// studio moments  --seed 7                          lists the moments and cards the engine finds
// studio crew     --seed 7 [--viewers analyst.en,casual.es]  prints the Control Room transcript (mock model)

var command = args.FirstOrDefault() ?? "summary";
var seed = int.Parse(Arg("--seed") ?? "7");
var match = MatchSimulator.Simulate(seed);

switch (command)
{
    case "generate":
        var outDir = Arg("--out") ?? Path.Combine("data", $"seed-{seed}");
        Directory.CreateDirectory(outDir);
        var indented = new JsonSerializerOptions(StudioJson.Options) { WriteIndented = true };
        // Explicit \n line endings so the committed dataset is byte-identical on every OS.
        File.WriteAllText(Path.Combine(outDir, "match.json"),
            JsonSerializer.Serialize(match.Info, indented).ReplaceLineEndings("\n") + "\n");
        File.WriteAllText(Path.Combine(outDir, "events.jsonl"),
            string.Concat(match.Events.Select(e => JsonSerializer.Serialize(e, StudioJson.Options) + "\n")));
        Console.WriteLine($"Wrote {match.Events.Count} events for {match.Info.MatchId} to {outDir}");
        break;

    case "summary":
        var state = new MatchState(match.Info);
        foreach (var e in match.Events) state.Apply(e);
        Console.WriteLine(JsonSerializer.Serialize(state.Snapshot(),
            new JsonSerializerOptions(StudioJson.Options) { WriteIndented = true }));
        break;

    case "moments":
        var pipeline = new MatchPipeline(match.Info);
        foreach (var e in match.Events)
            foreach (var card in pipeline.Process(e).Cards)
                Console.WriteLine($"{card.Minute,3}' {card.Kind,-14} P{card.Priority} {card.Headline} | {card.Body}");
        break;

    case "crew":
        var crewPipeline = new MatchPipeline(match.Info);
        var viewers = ViewerProfile.ParseList(Arg("--viewers") ?? "analyst.en,casual.es,club.fr.home,player.en.H10");
        // --azure <endpoint> --deployment <name> runs against Azure OpenAI with your az login; --limit N stops after N stories.
        var crewOptions = new CrewOptions
        {
            MaxConcurrentMoments = 100,
            MockLatencyMs = 0,
            Mode = Arg("--azure") is null ? "mock" : "azure",
            Endpoint = Arg("--azure"),
            Deployment = Arg("--deployment") ?? "gpt-4.1-mini",
        };
        var crew = new StudioCrew(ModelFactory.Create(crewOptions), crewOptions);
        var limit = int.Parse(Arg("--limit") ?? "1000");
        var stories = 0;
        foreach (var e in match.Events)
        {
            if (stories >= limit) break;
            var step = crewPipeline.Process(e);
            foreach (var m in step.Moments)
            {
                var route = crew.Gallery.Route(m);
                if (route.Route != CrewRoute.Crew) continue;
                if (++stories > limit) break;
                Console.WriteLine();
                Console.WriteLine($"-- {m.Minute}' {m.Kind} --");
                var template = step.Cards.First(c => c.MomentId == m.Id);
                var result = await crew.RunAsync(crew.Brief(m, crewPipeline.State), template, viewers, () => m.T,
                    msg => Console.WriteLine($"  {msg.Data?["elapsedMs"],6}ms {msg.From,-8} {msg.Text}"), CancellationToken.None);
                foreach (var v in result.Versions)
                    Console.WriteLine($"    [{v.Viewer.Id}] {v.Card.Headline} | {v.Card.Body}");
            }
        }
        break;

    case "commentary":
        var commentaryPipeline = new MatchPipeline(match.Info);
        var commentator = new Commentator(match.Info, Arg("--lang") ?? "en");
        foreach (var e in match.Events)
        {
            commentaryPipeline.Process(e);
            if (commentator.Call(e, commentaryPipeline.State) is { } line)
                Console.WriteLine($"{line.Minute,3}' {(line.Voiced ? "🔊" : "  ")} {new string('!', line.Excitement),-3} {line.Text}");
        }
        break;

    case "evaluate":
        // evaluate --azure <endpoint> [--deployment gpt-4.1-mini] [--seeds 3,7] [--per-seed 15] [--out ../docs/evaluation]
        if (args.Contains("--report-only"))
            return await Evaluation.ReportOnlyAsync(Arg("--out") ?? Path.Combine("..", "docs", "evaluation"),
                Arg("--deployment") ?? "gpt-4.1-mini", (Arg("--seeds") ?? "3,7").Split(',').Select(int.Parse).ToArray());
        if (Arg("--azure") is not { } evalEndpoint) { Console.Error.WriteLine("evaluate needs --azure <endpoint>"); return 1; }
        return await Evaluation.RunAsync(evalEndpoint, Arg("--deployment") ?? "gpt-4.1-mini",
            (Arg("--seeds") ?? "3,7").Split(',').Select(int.Parse).ToArray(), int.Parse(Arg("--per-seed") ?? "15"),
            Arg("--out") ?? Path.Combine("..", "docs", "evaluation"));

    case "dataset":
        // dataset [--seeds 1-100] [--events 1-5] [--out ../data/synthetic]
        return Dataset.Write(Dataset.ParseRange(Arg("--seeds") ?? "1-100"), Dataset.ParseRange(Arg("--events") ?? "1-5"),
            Arg("--out") ?? Path.Combine("..", "data", "synthetic"));

    case "workflow":
        // The crew's Agent Framework workflow, drawn by the framework itself.
        Console.WriteLine(Microsoft.Agents.AI.Workflows.WorkflowVisualizer.ToMermaidString(
            new StudioCrew(new ScriptedChatClient(0), new CrewOptions()).BuildWorkflow()));
        break;

    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Use generate, summary, moments, crew, commentary or workflow.");
        return 1;
}
return 0;

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

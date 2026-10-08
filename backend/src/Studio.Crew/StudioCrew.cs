using System.Text.Json;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Studio.Engine;

namespace Studio.Crew;

public sealed record CrewResult(InsightCard? Card, Verdict? Verdict, int Rounds, IReadOnlyList<HostVersion> Versions)
{
    public static CrewResult NotAired(Verdict? verdict, int rounds) => new(null, verdict, rounds, []);
}

/// <summary>Everything one moment's crew run carries from step to step through the workflow.</summary>
public sealed class CrewRun(Briefing briefing, InsightCard template, IReadOnlyList<ViewerProfile> viewers,
    Func<double> now, Action<CrewMessage> emit)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public Briefing Briefing { get; } = briefing;
    public InsightCard Template { get; } = template;
    public IReadOnlyList<ViewerProfile> Viewers { get; } = viewers;
    public Func<double> Now { get; } = now;

    public AgentSession? Session { get; set; }
    public ChatClientAgentRunOptions? GafferOptions { get; set; }
    public string Request { get; set; } = "";
    public int Rounds { get; set; }
    public StoryPitch? Pitch { get; set; }
    public Verdict? Verdict { get; set; }
    public bool Air { get; set; }
    public InsightCard? Card { get; set; }
    public IReadOnlyList<HostVersion> Versions { get; set; } = [];

    [MemberNotNullWhen(true, nameof(Pitch), nameof(Verdict))]
    public bool Approved => Verdict is { Approved: true } && Pitch is not null;

    /// <summary>Ref sends it back while the pitch is rejected and revisions remain.</summary>
    public bool CanRevise(int maxRevisions) => !Approved && Rounds <= maxRevisions;

    /// <summary>Report a step to the Control Room, stamped with time since the crew picked the moment up.</summary>
    public void Say(CrewRole from, CrewMessageKind kind, string text, Dictionary<string, object>? data = null)
    {
        data ??= [];
        data["elapsedMs"] = _clock.ElapsedMilliseconds;
        emit(new CrewMessage(Briefing.Moment.Id, from, kind, text, Now(), data));
    }
}

/// <summary>
/// The Virtual Studio Crew, as a Microsoft Agent Framework workflow over one moment:
/// Stats briefs → The Gaffer pitches → Ref checks (rulebook, then model) → back to The Gaffer
/// if rejected → Gallery decides whether it still airs → The Host writes it for each viewer →
/// Ref checks every version → on air.
/// Every step is reported through <see cref="CrewRun.Say"/>, which is what the Control Room shows.
/// </summary>
public sealed class StudioCrew
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AIAgent _gaffer;
    private readonly AIAgent _ref;
    private readonly StatsAnalyst _stats = new();
    private readonly HostAgent _host;
    private readonly CrewOptions _options;

    public StudioCrew(IChatClient chat, CrewOptions options, bool highlightsOnly = false)
    {
        _options = options;
        _gaffer = Agent(chat, CrewPrompts.Gaffer, "The Gaffer", "Tactician: pitches why a moment matters");
        _ref = Agent(chat, CrewPrompts.Ref, "Ref", "Fact-checker: approves or rejects pitches");
        _host = new HostAgent(chat);
        Gallery = new GalleryProducer(options, highlightsOnly);
    }

    public GalleryProducer Gallery { get; }

    /// <summary>
    /// An agent that can serve several moments at once: the crew works on up to
    /// <see cref="CrewOptions.MaxConcurrentMoments"/> in parallel, and The Host writes every viewer's version together.
    /// </summary>
    internal static AIAgent Agent(IChatClient chat, string instructions, string name, string description) =>
        new ChatClientAgent(chat, new ChatClientAgentOptions
        {
            Name = name,
            Description = description,
            ChatOptions = new ChatOptions { Instructions = instructions },
            AllowConcurrentInvocation = true,
        });

    public Briefing Brief(Moment m, MatchState state) => _stats.Brief(m, state);

    public Task<CrewResult> RunAsync(Briefing b, InsightCard template, Func<double> nowMatchT,
        Action<CrewMessage> emit, CancellationToken ct) => RunAsync(b, template, [], nowMatchT, emit, ct);

    /// <summary>Runs the crew workflow on one moment and returns what went on air.</summary>
    public async Task<CrewResult> RunAsync(Briefing b, InsightCard template, IReadOnlyList<ViewerProfile> viewers,
        Func<double> nowMatchT, Action<CrewMessage> emit, CancellationToken ct)
    {
        var job = new CrewRun(b, template, viewers, nowMatchT, emit);
        try
        {
            await using var run = await InProcessExecution.RunAsync(BuildWorkflow(), job, null, ct);
            foreach (var evt in run.OutgoingEvents)
            {
                switch (evt)
                {
                    case ExecutorFailedEvent failed:
                        throw new InvalidOperationException($"Crew step '{failed.ExecutorId}' failed.", failed.Data as Exception);
                    case WorkflowErrorEvent error:
                        throw new InvalidOperationException("The crew workflow failed.", error.Data as Exception);
                }
            }
            return job.Card is { } card
                ? new CrewResult(card, job.Verdict, job.Rounds, job.Versions)
                : CrewResult.NotAired(job.Verdict, job.Rounds);
        }
        finally
        {
            Gallery.Finished();
        }
    }

    /// <summary>
    /// The crew as a Microsoft Agent Framework workflow. Each character is an executor; the edges
    /// carry the editorial logic: Ref sends a pitch back to The Gaffer while revisions remain,
    /// and Gallery only passes a story to The Host if it's verified and still fresh.
    /// </summary>
    public Workflow BuildWorkflow()
    {
        var stats = Step("Stats", BriefAsync);
        var gaffer = Step("The Gaffer", PitchAsync);
        var referee = Step("Ref", CheckAsync);
        var gallery = Step("Gallery", DecideAsync);
        var host = Step("The Host", PresentAsync);
        // The last step's result is the workflow's output: what went on air (or why nothing did).
        ExecutorBinding onAir = new FunctionExecutor<CrewRun>("On air",
            (run, context, ct) => context.YieldOutputAsync(run, ct),
            outputTypes: [typeof(CrewRun)]);

        return new WorkflowBuilder(stats)
            .WithName("Virtual Studio Crew")
            .WithDescription("Stats briefs, The Gaffer pitches, Ref checks, Gallery decides, The Host presents.")
            .AddEdge(stats, gaffer)
            .AddEdge(gaffer, referee)
            .AddEdge<CrewRun>(referee, gaffer, r => r!.CanRevise(_options.MaxRevisions), "sent back")
            .AddEdge<CrewRun>(referee, gallery, r => !r!.CanRevise(_options.MaxRevisions), "verdict")
            .AddEdge<CrewRun>(gallery, host, r => r!.Air, "airs")
            .AddEdge<CrewRun>(gallery, onAir, r => !r!.Air, "dropped")
            .AddEdge(host, onAir)
            .WithOutputFrom(onAir)
            .Build();
    }

    private static ExecutorBinding Step(string id, Func<CrewRun, CancellationToken, Task> step) =>
        ((Func<CrewRun, IWorkflowContext, CancellationToken, ValueTask<CrewRun>>)(async (run, _, ct) =>
        {
            await step(run, ct);
            return run;
        })).BindAsExecutor(id);

    private async Task BriefAsync(CrewRun run, CancellationToken ct)
    {
        var (m, sheet) = (run.Briefing.Moment, run.Briefing.Sheet);
        run.Say(CrewRole.Stats, CrewMessageKind.Brief,
            $"{sheet.Facts.Count} facts on the sheet for the {Describe(m.Kind)} ({m.Minute}').",
            new() { ["facts"] = sheet.Facts.Count });
        var tools = _stats.Tools(sheet, run.Briefing.Snapshot, run.Briefing.Info,
            what => run.Say(CrewRole.Stats, CrewMessageKind.ToolCall, $"The Gaffer asked for more: {what}."));
        run.Session = await _gaffer.CreateSessionAsync(ct);
        run.GafferOptions = new ChatClientAgentRunOptions(new ChatOptions
        {
            Tools = tools,
            ResponseFormat = ChatResponseFormat.Json,
            MaxOutputTokens = 400,
            Temperature = 0.4f,
        });
        run.Request = CrewPrompts.PitchRequest(sheet);
    }

    private async Task PitchAsync(CrewRun run, CancellationToken ct)
    {
        run.Rounds++;
        var response = await _gaffer.RunAsync(run.Request, run.Session, run.GafferOptions, ct);
        run.Pitch = Parse<StoryPitch>(response.Text);
        run.Verdict = null;
        if (run.Pitch is { } pitch)
            run.Say(CrewRole.Gaffer, CrewMessageKind.Pitch, $"{pitch.Headline}. {pitch.Body}",
                new() { ["headline"] = pitch.Headline, ["body"] = pitch.Body, ["claims"] = pitch.Claims, ["round"] = run.Rounds });
    }

    private async Task CheckAsync(CrewRun run, CancellationToken ct)
    {
        var sheet = run.Briefing.Sheet;
        if (run.Pitch is not { } pitch)
        {
            run.Verdict = new Verdict(false, ["The pitch wasn't valid JSON."], 0, 0);
            run.Say(CrewRole.Ref, CrewMessageKind.Verdict, "Can't read that pitch. Send it again as JSON.", VerdictData(run.Verdict));
        }
        else
        {
            var verdict = RefChecker.Check(pitch, sheet);
            if (verdict.Approved && await Review(sheet, pitch, ct) is { Approved: false } review)
                verdict = verdict with { Approved = false, Reasons = review.Reasons ?? ["Ref isn't convinced."] };
            run.Verdict = verdict;
            run.Say(CrewRole.Ref, CrewMessageKind.Verdict, verdict.Approved
                    ? $"Checked {Plural(verdict.ClaimsChecked, "claim")} and {Plural(verdict.NumbersChecked, "number")}. Clean. Approved."
                    : $"Rejected. {string.Join(" ", verdict.Reasons)}",
                VerdictData(verdict));
        }
        if (!run.Approved) run.Request = CrewPrompts.RevisionRequest(sheet, run.Verdict!.Reasons);
    }

    private Task DecideAsync(CrewRun run, CancellationToken _)
    {
        if (!run.Approved)
        {
            run.Say(CrewRole.Gallery, CrewMessageKind.Decision, "Ref never cleared it. The template graphic stays; nothing unverified goes on air.",
                new() { ["air"] = false });
            return Task.CompletedTask;
        }
        var air = Gallery.Decide(run.Briefing.Moment, run.Now());
        run.Air = air.Air;
        run.Say(CrewRole.Gallery, CrewMessageKind.Decision, air.Reason, new() { ["air"] = air.Air });
        return Task.CompletedTask;
    }

    private async Task PresentAsync(CrewRun run, CancellationToken ct)
    {
        var card = HostAgent.Studio(run.Pitch!, run.Template);
        run.Versions = await PresentToViewers(run.Pitch!, run.Briefing, run.Template, run.Viewers, run.Say, ct);
        var audience = run.Versions.Count == 0 ? "the studio feed"
            : "the studio feed and " + string.Join(", ", run.Versions.Select(v => $"{Label(v.Viewer)} ({v.Viewer.Language.ToUpperInvariant()})"));
        run.Say(CrewRole.Host, CrewMessageKind.OnAir, $"On air for {audience}: {card.Headline}",
            new() { ["cardId"] = card.Id, ["viewers"] = run.Versions.Count });
        run.Card = card;
    }

    private async Task<IReadOnlyList<HostVersion>> PresentToViewers(StoryPitch pitch, Briefing b, InsightCard template,
        IReadOnlyList<ViewerProfile> viewers, Action<CrewRole, CrewMessageKind, string, Dictionary<string, object>?> say,
        CancellationToken ct)
    {
        var audience = viewers.Where(v => Relevance.Shows(v, b.Moment)).ToList();
        if (audience.Count == 0) return [];
        foreach (var v in audience.Where(v => v.PlayerId is not null))
            StatsAnalyst.AddFocusPlayer(b, v.PlayerId!);

        var versions = await Task.WhenAll(audience.Select(v => _host.PresentAsync(pitch, b, template, v, ct)));
        var failed = versions.Where(v => !v.Verified).ToList();
        var languages = versions.Select(v => v.Viewer.Language).Distinct().Count();
        say(CrewRole.Ref, CrewMessageKind.Verdict, failed.Count == 0
                ? $"Checked {Plural(versions.Length, "version")} in {Plural(languages, "language")}, {Plural(versions.Sum(v => v.NumbersChecked), "number")}. All clean."
                : $"{failed.Count} of {versions.Length} versions failed ({string.Join(" ", failed.SelectMany(f => f.Reasons).Distinct())}). Those viewers keep the template.",
            new()
            {
                ["approved"] = failed.Count == 0,
                ["claimsChecked"] = 0,
                ["numbersChecked"] = versions.Sum(v => v.NumbersChecked),
                ["versions"] = versions.Length,
                ["stage"] = "host",
            });
        return versions;
    }

    private static string Label(ViewerProfile v) => v.Persona switch
    {
        Persona.Analyst => "Analyst",
        Persona.Casual => "Casual",
        Persona.ClubFan => "Club fan",
        Persona.PlayerFocus => "Player focus",
        Persona.MetricFocus => $"Metric focus: {v.Metric}",
        _ => v.Id,
    };

    private async Task<ReviewDto?> Review(FactSheet sheet, StoryPitch pitch, CancellationToken ct)
    {
        var options = new ChatClientAgentRunOptions(new ChatOptions { ResponseFormat = ChatResponseFormat.Json, MaxOutputTokens = 250, Temperature = 0f });
        var response = await _ref.RunAsync(CrewPrompts.ReviewRequest(sheet, pitch), null, options, ct);
        return Parse<ReviewDto>(response.Text);
    }

    private static Dictionary<string, object> VerdictData(Verdict v) => new()
    {
        ["approved"] = v.Approved,
        ["claimsChecked"] = v.ClaimsChecked,
        ["numbersChecked"] = v.NumbersChecked,
        ["reasons"] = v.Reasons,
    };

    internal static T? Parse<T>(string? text) where T : class
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try { return JsonSerializer.Deserialize<T>(text[start..(end + 1)], Json); }
        catch (JsonException) { return null; }
    }

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string Describe(MomentKind kind) => kind switch
    {
        MomentKind.Goal => "goal",
        MomentKind.BigChance => "big chance",
        MomentKind.PressSurge => "pressing surge",
        MomentKind.MomentumSwing => "momentum swing",
        MomentKind.ChaosSpell => "spell of chaos",
        MomentKind.ControlSpell => "spell of control",
        MomentKind.ElitePass => "standout pass",
        _ => kind.ToString(),
    };
}

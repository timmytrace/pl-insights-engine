using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Studio.Engine;

namespace Studio.Crew;

public sealed record CrewResult(InsightCard? Card, Verdict? Verdict, int Rounds, IReadOnlyList<HostVersion> Versions)
{
    public static CrewResult NotAired(Verdict? verdict, int rounds) => new(null, verdict, rounds, []);
}

/// <summary>
/// Runs the Virtual Studio Crew on one moment:
/// Stats briefs → The Gaffer pitches → Ref checks (rulebook, then model) → back to The Gaffer
/// if rejected → Gallery decides whether it still airs → The Host writes it for each viewer →
/// Ref checks every version → on air.
/// Every step is reported through <c>emit</c>, which is what the Control Room shows.
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

    public async Task<CrewResult> RunAsync(Briefing b, InsightCard template, IReadOnlyList<ViewerProfile> viewers,
        Func<double> nowMatchT, Action<CrewMessage> emit, CancellationToken ct)
    {
        var m = b.Moment;
        var sheet = b.Sheet;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Say(CrewRole from, CrewMessageKind kind, string text, Dictionary<string, object>? data = null)
        {
            data ??= [];
            data["elapsedMs"] = clock.ElapsedMilliseconds;   // time since the crew picked the moment up
            emit(new CrewMessage(m.Id, from, kind, text, nowMatchT(), data));
        }

        try
        {
            Say(CrewRole.Stats, CrewMessageKind.Brief,
                $"{sheet.Facts.Count} facts on the sheet for the {Describe(m.Kind)} ({m.Minute}').",
                new() { ["facts"] = sheet.Facts.Count });

            var tools = _stats.Tools(sheet, b.Snapshot, b.Info,
                what => Say(CrewRole.Stats, CrewMessageKind.ToolCall, $"The Gaffer asked for more: {what}."));
            var session = await _gaffer.CreateSessionAsync(ct);
            var runOptions = new ChatClientAgentRunOptions(new ChatOptions
            {
                Tools = tools,
                ResponseFormat = ChatResponseFormat.Json,
                MaxOutputTokens = 400,
                Temperature = 0.4f,
            });

            var request = CrewPrompts.PitchRequest(sheet);
            StoryPitch? pitch = null;
            Verdict? verdict = null;
            var rounds = 0;
            while (rounds <= _options.MaxRevisions)
            {
                rounds++;
                var response = await _gaffer.RunAsync(request, session, runOptions, ct);
                pitch = Parse<StoryPitch>(response.Text);
                if (pitch is null)
                {
                    verdict = new Verdict(false, ["The pitch wasn't valid JSON."], 0, 0);
                    Say(CrewRole.Ref, CrewMessageKind.Verdict, "Can't read that pitch. Send it again as JSON.", VerdictData(verdict));
                    request = CrewPrompts.RevisionRequest(sheet, verdict.Reasons);
                    continue;
                }

                Say(CrewRole.Gaffer, CrewMessageKind.Pitch, $"{pitch.Headline}. {pitch.Body}",
                    new() { ["headline"] = pitch.Headline, ["body"] = pitch.Body, ["claims"] = pitch.Claims, ["round"] = rounds });

                verdict = RefChecker.Check(pitch, sheet);
                if (verdict.Approved)
                {
                    var review = await Review(sheet, pitch, ct);
                    if (review is { Approved: false })
                        verdict = verdict with { Approved = false, Reasons = review.Reasons ?? ["Ref isn't convinced."] };
                }

                Say(CrewRole.Ref, CrewMessageKind.Verdict, verdict.Approved
                        ? $"Checked {Plural(verdict.ClaimsChecked, "claim")} and {Plural(verdict.NumbersChecked, "number")}. Clean. Approved."
                        : $"Rejected. {string.Join(" ", verdict.Reasons)}",
                    VerdictData(verdict));
                if (verdict.Approved) break;
                request = CrewPrompts.RevisionRequest(sheet, verdict.Reasons);
            }

            if (verdict is not { Approved: true } || pitch is null)
            {
                Say(CrewRole.Gallery, CrewMessageKind.Decision, "Ref never cleared it. The template graphic stays; nothing unverified goes on air.",
                    new() { ["air"] = false });
                return CrewResult.NotAired(verdict, rounds);
            }

            var air = Gallery.Decide(m, nowMatchT());
            Say(CrewRole.Gallery, CrewMessageKind.Decision, air.Reason, new() { ["air"] = air.Air });
            if (!air.Air) return CrewResult.NotAired(verdict, rounds);

            var card = HostAgent.Studio(pitch, template);
            var versions = await PresentToViewers(pitch, b, template, viewers, Say, ct);
            var audience = versions.Count == 0 ? "the studio feed"
                : "the studio feed and " + string.Join(", ", versions.Select(v => $"{Label(v.Viewer)} ({v.Viewer.Language.ToUpperInvariant()})"));
            Say(CrewRole.Host, CrewMessageKind.OnAir, $"On air for {audience}: {card.Headline}",
                new() { ["cardId"] = card.Id, ["viewers"] = versions.Count });
            return new CrewResult(card, verdict, rounds, versions);
        }
        finally
        {
            Gallery.Finished();
        }
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

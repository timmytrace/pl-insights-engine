using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Studio.Engine;

namespace Studio.Crew;

public sealed record CrewResult(InsightCard? Card, Verdict? Verdict, int Rounds);

/// <summary>
/// Runs the Virtual Studio Crew on one moment:
/// Stats briefs → The Gaffer pitches → Ref checks (rulebook, then model) → back to The Gaffer
/// if rejected → Gallery decides whether it still airs → The Host puts it on screen.
/// Every step is reported through <c>emit</c>, which is what the Control Room shows.
/// </summary>
public sealed class StudioCrew
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AIAgent _gaffer;
    private readonly AIAgent _ref;
    private readonly StatsAnalyst _stats = new();
    private readonly CrewOptions _options;

    public StudioCrew(IChatClient chat, CrewOptions options)
    {
        _options = options;
        _gaffer = chat.AsAIAgent(CrewPrompts.Gaffer, "The Gaffer", "Tactician: pitches why a moment matters");
        _ref = chat.AsAIAgent(CrewPrompts.Ref, "Ref", "Fact-checker: approves or rejects pitches");
        Gallery = new GalleryProducer(options);
    }

    public GalleryProducer Gallery { get; }

    public Briefing Brief(Moment m, MatchState state) => _stats.Brief(m, state);

    public async Task<CrewResult> RunAsync(Briefing b, InsightCard template, Func<double> nowMatchT,
        Action<CrewMessage> emit, CancellationToken ct)
    {
        var m = b.Moment;
        var sheet = b.Sheet;
        void Say(CrewRole from, CrewMessageKind kind, string text, Dictionary<string, object>? data = null) =>
            emit(new CrewMessage(m.Id, from, kind, text, nowMatchT(), data));

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
                return new CrewResult(null, verdict, rounds);
            }

            var air = Gallery.Decide(m, nowMatchT());
            Say(CrewRole.Gallery, CrewMessageKind.Decision, air.Reason, new() { ["air"] = air.Air });
            if (!air.Air) return new CrewResult(null, verdict, rounds);

            var card = Host.Present(pitch, template);
            Say(CrewRole.Host, CrewMessageKind.OnAir, $"On air: {card.Headline}", new() { ["cardId"] = card.Id });
            return new CrewResult(card, verdict, rounds);
        }
        finally
        {
            Gallery.Finished();
        }
    }

    private async Task<ReviewDto?> Review(FactSheet sheet, StoryPitch pitch, CancellationToken ct)
    {
        var options = new ChatClientAgentRunOptions(new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
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

/// <summary>The Host: presents approved stories. Phase 3 adds personas and languages here.</summary>
public static class Host
{
    public static InsightCard Present(StoryPitch pitch, InsightCard template) => template with
    {
        Id = template.Id.Replace("-C", "-A"),
        Headline = pitch.Headline,
        Body = pitch.Body,
        Source = "agent",
        Replaces = template.Id,
    };
}

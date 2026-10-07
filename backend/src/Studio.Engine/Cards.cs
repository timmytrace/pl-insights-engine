namespace Studio.Engine;

public sealed record CardStat(string Label, string Value, string? Unit = null);

/// <summary>
/// A timed, machine-readable overlay graphic. A rendering partner can schedule it against
/// the match feed from ShowAtT/DurationS and place it from Slot without parsing any prose.
/// </summary>
public sealed record InsightCard(
    string Id,
    string MomentId,
    string MatchId,
    MomentKind Kind,
    Side Team,
    string? PlayerId,
    double ShowAtT,
    double Clock,
    int Minute,
    double DurationS,
    int Priority,           // 1 (must show) .. 5 (filler)
    string Slot,            // lower_third | player_tag | corner | banner
    string Headline,
    string Body,
    IReadOnlyList<CardStat> Stats,
    IReadOnlyList<string> Evidence,
    string Persona,
    string Language,
    string Source,          // template | agent
    string? Replaces = null);  // the template card an agent card upgrades

/// <summary>Turns a moment into cards. The agent crew implements this in the next phase.</summary>
public interface ICardWriter
{
    IReadOnlyList<InsightCard> Write(Moment moment, MatchState state);
}

/// <summary>
/// Deterministic English cards built straight from moment facts. This is the baseline the
/// overlay is built against, and the fallback when the AI service is unavailable.
/// </summary>
public sealed class TemplateCardWriter : ICardWriter
{
    public IReadOnlyList<InsightCard> Write(Moment m, MatchState state)
    {
        var team = state.Info.Team(m.Team);
        var name = m.PlayerName ?? team.Name;
        string F(string key) => m.Facts.TryGetValue(key, out var v) ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";
        double D(string key) => m.Facts.TryGetValue(key, out var v) ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) : 0;

        var (slot, priority, duration, headline, body, stats) = m.Kind switch
        {
            MomentKind.Goal => ("banner", 1, 12.0, $"GOAL — {name}",
                $"{state.Info.Home.ShortName} {F("homeScore")}–{F("awayScore")} {state.Info.Away.ShortName}. "
                + (D("xg") < 0.1 ? "A low-percentage finish." : D("xg") > 0.4 ? "A chance this good is usually taken." : "A fair chance, well taken.")
                + (F("assistName") is { Length: > 0 } a ? $" Created by {a}." : ""),
                new[] { new CardStat("xG", D("xg").ToString("0.00")), new CardStat("Shot speed", D("shotSpeedKmh").ToString("0"), "km/h"), new CardStat("Distance", D("distanceM").ToString("0"), "m") }),
            MomentKind.BigChance => ("lower_third", 2, 8.0, $"Big chance — {name}",
                $"A {D("xg"):0.00} xG opportunity ends {F("outcome").Replace('_', ' ')}.",
                new[] { new CardStat("xG", D("xg").ToString("0.00")), new CardStat("Distance", D("distanceM").ToString("0"), "m") }),
            MomentKind.ShotSpeed => ("player_tag", 3, 6.0, name, "Shot speed",
                new[] { new CardStat("Shot speed", D("shotSpeedKmh").ToString("0"), "km/h") }),
            MomentKind.SprintSpeed => ("player_tag", 4, 5.0, name, "Top-speed sprint",
                new[] { new CardStat("Speed", D("speedKmh").ToString("0.0"), "km/h"), new CardStat("Sprint", D("distanceM").ToString("0"), "m") }),
            MomentKind.ElitePass => ("lower_third", 3, 7.0, $"Pass quality — {name}",
                $"A {D("difficulty") * 100:0}/100 difficulty pass to {F("receiverName")}" + (F("underPressure") == "True" ? " under pressure." : "."),
                new[] { new CardStat("Difficulty", (D("difficulty") * 100).ToString("0"), "/100"), new CardStat("Distance", D("distanceM").ToString("0"), "m"), new CardStat("Ball speed", D("ballSpeedKmh").ToString("0"), "km/h") }),
            MomentKind.Milestone => ("corner", 4, 6.0, name, MilestoneText(F("milestone"), m),
                Array.Empty<CardStat>()),
            MomentKind.PressSurge => ("lower_third", 2, 9.0, $"{team.Name} turn up the press",
                $"Allowing {D("ppda10"):0.0} passes per defensive action over the last 10 minutes, against {D("matchPpda"):0.0} for the match.",
                new[] { new CardStat("PPDA (10 min)", D("ppda10").ToString("0.0")), new CardStat("PPDA (match)", D("matchPpda").ToString("0.0")) }),
            MomentKind.MomentumSwing => ("lower_third", 2, 8.0, $"Momentum swings to {team.Name}",
                $"Momentum moves from {D("previousMomentum"):+0;-0} to {D("momentum"):+0;-0} on the home–away scale.",
                new[] { new CardStat("Momentum", D("momentum").ToString("+0;-0")) }),
            MomentKind.ChaosSpell => ("corner", 3, 8.0, "Control vs. chaos: chaos",
                $"{D("turnoversPerMin"):0.0} turnovers a minute. Neither side can keep the ball.",
                new[] { new CardStat("Chaos index", D("chaos").ToString("0")), new CardStat("Turnovers / min", D("turnoversPerMin").ToString("0.0")) }),
            MomentKind.ControlSpell => ("corner", 3, 8.0, $"Control vs. chaos: {team.ShortName} in control",
                $"{team.Name} are dictating the last five minutes.",
                new[] { new CardStat("Control index", D("control").ToString("0")), new CardStat("Tempo", D("tempoPassesPerMin").ToString("0.0"), "passes/min") }),
            _ => ("corner", 5, 5.0, name, m.Kind.ToString(), Array.Empty<CardStat>()),
        };

        return
        [
            new InsightCard($"{m.Id}-C1", m.Id, state.Info.MatchId, m.Kind, m.Team, m.PlayerId, m.T, m.Clock,
                m.Minute, duration, priority, slot, headline, body, stats, m.Evidence, "neutral", "en", "template"),
        ];
    }

    private static string MilestoneText(string milestone, Moment m) => milestone switch
    {
        "hat_trick" => "Hat-trick!",
        "brace" => "Second goal of the match",
        "passes_completed" => $"{m.Facts["passesCompleted"]} completed passes ({m.Facts["accuracyPct"]}% accuracy)",
        _ => milestone,
    };
}

/// <summary>Everything one event produced, in the order the overlay should receive it.</summary>
public sealed record PipelineStep(EventMetrics Metrics, IReadOnlyList<Moment> Moments, IReadOnlyList<InsightCard> Cards);

/// <summary>Ingest → interpret → detect → write, one event at a time.</summary>
public sealed class MatchPipeline
{
    private readonly MomentDetector _detector;
    private readonly ICardWriter _writer;

    public MatchPipeline(MatchInfo info, ICardWriter? writer = null)
    {
        State = new MatchState(info);
        _detector = new MomentDetector(State);
        _writer = writer ?? new TemplateCardWriter();
    }

    public MatchState State { get; }

    public PipelineStep Process(MatchEvent e)
    {
        var metrics = State.Apply(e);
        var moments = _detector.Observe(metrics);
        var cards = moments.SelectMany(m => _writer.Write(m, State)).ToList();
        return new PipelineStep(metrics, moments, cards);
    }
}

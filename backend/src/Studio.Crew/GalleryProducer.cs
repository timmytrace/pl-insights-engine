using Studio.Engine;

namespace Studio.Crew;

public enum CrewRoute { TemplateOnly, Crew }

public sealed record RoutingDecision(CrewRoute Route, string Reason);

public sealed record AirDecision(bool Air, string Reason);

/// <summary>
/// Gallery, the Producer. Live television runs on a budget: the crew can only work on so many
/// moments at once, and a story that arrives after play has moved on is worse than none.
/// Gallery makes those calls with explicit rules and says why, so every decision is on record.
/// </summary>
public sealed class GalleryProducer(CrewOptions options)
{
    private int _inFlight;

    public int InFlight => Volatile.Read(ref _inFlight);

    public RoutingDecision Route(Moment m)
    {
        switch (m.Kind)
        {
            case MomentKind.ShotSpeed:
            case MomentKind.SprintSpeed:
                return new(CrewRoute.TemplateOnly, "Speed tag. The number is the story; straight to air.");
            case MomentKind.Milestone:
                return new(CrewRoute.TemplateOnly, "Milestone tag; straight to air.");
        }
        if (Interlocked.Increment(ref _inFlight) > options.MaxConcurrentMoments)
        {
            Interlocked.Decrement(ref _inFlight);
            return m.Kind == MomentKind.Goal
                ? new(CrewRoute.TemplateOnly, "Crew is full and goals can't wait; template goes out.")
                : new(CrewRoute.TemplateOnly, $"Crew is busy with {options.MaxConcurrentMoments} moments; template stays on air.");
        }
        return new(CrewRoute.Crew, m.Kind == MomentKind.Goal
            ? "Goal. Template on air now; crew, give me the story."
            : "Template on air now; crew, tell me why this matters.");
    }

    public void Finished() => Interlocked.Decrement(ref _inFlight);

    /// <summary>Should an approved card still go on air, given how far the match has moved on?</summary>
    public AirDecision Decide(Moment m, double nowMatchT)
    {
        var lag = nowMatchT - m.T;
        var budget = m.Kind == MomentKind.Goal ? options.GoalFreshnessSeconds : options.FreshnessSeconds;
        return lag <= budget
            ? new(true, $"Approved and fresh ({lag:0}s of play since). Upgrading the graphic.")
            : new(false, $"Approved, but {lag:0}s of play have passed (budget {budget:0}s). Dropping it: the moment has gone.");
    }
}

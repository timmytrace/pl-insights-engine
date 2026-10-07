using System.Collections.Concurrent;
using Studio.Engine;

namespace Studio.Api;

public sealed record MatchSummary(
    MatchInfo Info,
    MatchSnapshot FullTime,
    IReadOnlyList<Moment> Moments,
    IReadOnlyList<InsightCard> Cards);

/// <summary>Simulated matches are deterministic, so each seed is generated once and cached.</summary>
public sealed class MatchLibrary
{
    private readonly ConcurrentDictionary<int, Match> _matches = new();

    public Match Get(int seed) => _matches.GetOrAdd(seed, MatchSimulator.Simulate);

    public MatchSummary Summary(int seed)
    {
        var match = Get(seed);
        var pipeline = new MatchPipeline(match.Info);
        var moments = new List<Moment>();
        var cards = new List<InsightCard>();
        foreach (var e in match.Events)
        {
            var step = pipeline.Process(e);
            moments.AddRange(step.Moments);
            cards.AddRange(step.Cards);
        }
        return new MatchSummary(match.Info, pipeline.State.Snapshot(), moments, cards);
    }
}

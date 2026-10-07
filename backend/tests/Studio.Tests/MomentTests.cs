using Studio.Engine;

namespace Studio.Tests;

public class MomentTests
{
    private static (Match Match, List<Moment> Moments, List<InsightCard> Cards) Run(int seed)
    {
        var match = MatchSimulator.Simulate(seed);
        var pipeline = new MatchPipeline(match.Info);
        var moments = new List<Moment>();
        var cards = new List<InsightCard>();
        foreach (var e in match.Events)
        {
            var step = pipeline.Process(e);
            moments.AddRange(step.Moments);
            cards.AddRange(step.Cards);
        }
        return (match, moments, cards);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(19)]
    public void Every_goal_becomes_a_goal_moment(int seed)
    {
        var (match, moments, _) = Run(seed);
        var goals = match.Events.Count(e => e is { Type: EventType.Shot, Outcome: "goal" });
        Assert.Equal(goals, moments.Count(m => m.Kind == MomentKind.Goal));
    }

    [Fact]
    public void Evidence_points_at_real_events_up_to_the_moment()
    {
        var (match, moments, _) = Run(7);
        var byId = match.Events.ToDictionary(e => e.Id);
        Assert.All(moments, m =>
        {
            Assert.NotEmpty(m.Evidence);
            Assert.All(m.Evidence, id => Assert.True(byId[id].T <= m.T));
        });
    }

    [Fact]
    public void Speed_tags_respect_their_thresholds()
    {
        var (_, moments, _) = Run(7);
        Assert.All(moments.Where(m => m.Kind == MomentKind.SprintSpeed),
            m => Assert.True((double)m.Facts["speedKmh"] >= Thresholds.SprintSpeedKmh));
        Assert.All(moments.Where(m => m.Kind == MomentKind.ShotSpeed),
            m => Assert.True((double)m.Facts["shotSpeedKmh"] >= Thresholds.ShotSpeedKmh));
    }

    [Fact]
    public void The_scripted_pressing_switch_is_found_from_the_data()
    {
        // Across a handful of matches the engine should spot at least one second-half press surge.
        var found = new[] { 1, 2, 3, 4, 5, 6, 7, 8 }
            .SelectMany(seed => Run(seed).Moments)
            .Any(m => m.Kind == MomentKind.PressSurge && m.Clock > 50 * 60);
        Assert.True(found);
    }

    [Fact]
    public void Cards_are_timed_and_renderable()
    {
        var (_, moments, cards) = Run(7);
        Assert.Equal(moments.Count, cards.Count);
        Assert.All(cards, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Headline));
            Assert.InRange(c.Priority, 1, 5);
            Assert.InRange(c.DurationS, 3, 15);
            Assert.Contains(c.Slot, new[] { "lower_third", "player_tag", "corner", "banner" });
        });
    }
}

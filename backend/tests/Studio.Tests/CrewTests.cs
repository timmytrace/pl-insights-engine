using Studio.Crew;
using Studio.Engine;

namespace Studio.Tests;

public class RefCheckerTests
{
    private static FactSheet Sheet()
    {
        var sheet = new FactSheet("M1", "Goal", 22, 1300);
        sheet.Add("xg", "xG", 0.91);
        sheet.Add("team_shots", "Shots", 7, isCount: true);
        sheet.Add("difficulty", "Pass difficulty", 0.734);
        sheet.Add("team_possession", "Possession", 63.4, "%");
        sheet.Add("momentum", "Momentum", -55.3);
        sheet.Add("minute", "Minute", 22, isCount: true);
        return sheet;
    }

    private static StoryPitch P(string body, params Claim[] claims) => new("Headline", body, claims);

    [Theory]
    [InlineData("A 0.91 xG chance")]
    [InlineData("A 0.9 xG chance")]               // rounded
    [InlineData("7 shots")]
    [InlineData("73/100 difficulty")]             // fraction quoted out of 100
    [InlineData("63% possession")]                // truncated
    [InlineData("63.4% possession")]
    [InlineData("momentum at -55.3")]             // signed value
    [InlineData("in the 22nd minute")]
    public void Numbers_on_the_sheet_pass(string body)
    {
        var v = RefChecker.Check(P(body, new Claim("c", ["xg"])), Sheet());
        Assert.True(v.Approved, string.Join(" ", v.Reasons));
    }

    [Theory]
    [InlineData("A 0.5 xG chance")]
    [InlineData("8 shots")]                       // counts must be exact
    [InlineData("70% possession")]
    [InlineData("12 passes")]
    public void Numbers_not_on_the_sheet_fail(string body)
    {
        Assert.False(RefChecker.Check(P(body, new Claim("c", ["xg"])), Sheet()).Approved);
    }

    [Theory]
    [InlineData("Best spell of the season.")]
    [InlineData("They always score from here.")]
    [InlineData("A club record.")]
    public void Claims_beyond_the_data_fail(string body)
    {
        var v = RefChecker.Check(P(body, new Claim("c", ["xg"])), Sheet());
        Assert.False(v.Approved);
    }

    [Fact]
    public void Citations_may_carry_their_group_name()
    {
        Assert.True(RefChecker.Check(P("A 0.91 xG chance.", new Claim("c", ["about_this_moment.xg"])), Sheet()).Approved);
    }

    [Fact]
    public void Internal_fact_names_never_reach_the_screen()
    {
        var v = RefChecker.Check(P("Seven shots (team_shots).", new Claim("c", ["team_shots"])), Sheet());
        Assert.False(v.Approved);
        Assert.Contains(v.Reasons, r => r.Contains("internal fact name"));
    }

    [Fact]
    public void Match_totals_are_too_early_to_quote_in_the_first_minutes()
    {
        var early = new FactSheet("M", "ElitePass", 2, 100);
        early.Add("team_possession", "Possession", 89.5, "%", matchTotal: true);
        Assert.False(RefChecker.Check(P("89.5% possession.", new Claim("c", ["team_possession"])), early).Approved);

        var later = new FactSheet("M", "ElitePass", 30, 1800);
        later.Add("team_possession", "Possession", 89.5, "%", matchTotal: true);
        Assert.True(RefChecker.Check(P("89.5% possession.", new Claim("c", ["team_possession"])), later).Approved);
    }

    [Fact]
    public void Claims_must_cite_real_facts()
    {
        Assert.False(RefChecker.Check(P("Fine.", new Claim("c", [])), Sheet()).Approved);
        Assert.False(RefChecker.Check(P("Fine.", new Claim("c", ["made_up"])), Sheet()).Approved);
    }
}

public class CrewTests
{
    private static readonly CrewOptions Options = new() { MockLatencyMs = 0, MaxConcurrentMoments = 100 };

    private static async Task<List<(Moment Moment, CrewResult Result, List<CrewMessage> Log)>> RunMatch(int seed)
    {
        var match = MatchSimulator.Simulate(seed);
        var pipeline = new MatchPipeline(match.Info);
        var crew = new StudioCrew(new ScriptedChatClient(0), Options);
        var runs = new List<(Moment, CrewResult, List<CrewMessage>)>();
        foreach (var e in match.Events)
        {
            var step = pipeline.Process(e);
            foreach (var m in step.Moments)
            {
                if (crew.Gallery.Route(m).Route != CrewRoute.Crew) continue;
                var log = new List<CrewMessage>();
                var template = step.Cards.First(c => c.MomentId == m.Id);
                var result = await crew.RunAsync(crew.Brief(m, pipeline.State), template, () => m.T, log.Add, CancellationToken.None);
                runs.Add((m, result, log));
            }
        }
        return runs;
    }

    [Fact]
    public async Task Every_story_moment_ends_on_air_with_a_verified_card()
    {
        var runs = await RunMatch(7);
        Assert.NotEmpty(runs);
        Assert.All(runs, r =>
        {
            Assert.NotNull(r.Result.Card);
            Assert.Equal("agent", r.Result.Card!.Source);
            Assert.EndsWith("-C1", r.Result.Card.Replaces);
            Assert.Equal(CrewRole.Host, r.Log[^1].From);
        });
    }

    [Fact]
    public async Task Ref_sends_overreach_back_and_the_revision_is_clean()
    {
        var runs = await RunMatch(7);
        var revised = runs.Where(r => r.Result.Rounds > 1).ToList();
        Assert.NotEmpty(revised);
        foreach (var r in revised)
        {
            var verdicts = r.Log.Where(l => l.Kind == CrewMessageKind.Verdict).ToList();
            Assert.False((bool)verdicts[0].Data!["approved"]);
            Assert.True((bool)verdicts[^1].Data!["approved"]);
        }
    }

    [Fact]
    public async Task The_crew_speaks_in_order()
    {
        var log = (await RunMatch(7)).First().Log;
        Assert.Equal(CrewRole.Stats, log[0].From);
        Assert.Equal(CrewRole.Gaffer, log[1].From);
        Assert.Equal(CrewRole.Ref, log[2].From);
        Assert.Contains(log, l => l.From == CrewRole.Gallery);
    }

    [Fact]
    public void Gallery_sends_tags_straight_to_air_and_respects_its_budget()
    {
        var gallery = new GalleryProducer(new CrewOptions { MaxConcurrentMoments = 1 });
        Moment M(MomentKind kind) => new("M", kind, 100, 100, 2, Side.Home, null, null, 0.5, ["e"], new Dictionary<string, object>());

        Assert.Equal(CrewRoute.TemplateOnly, gallery.Route(M(MomentKind.SprintSpeed)).Route);
        Assert.Equal(CrewRoute.Crew, gallery.Route(M(MomentKind.PressSurge)).Route);
        Assert.Equal(CrewRoute.TemplateOnly, gallery.Route(M(MomentKind.MomentumSwing)).Route); // busy
        gallery.Finished();
        Assert.Equal(CrewRoute.Crew, gallery.Route(M(MomentKind.MomentumSwing)).Route);
    }

    [Fact]
    public void Gallery_drops_stories_that_arrive_too_late()
    {
        var gallery = new GalleryProducer(new CrewOptions { FreshnessSeconds = 120 });
        var m = new Moment("M", MomentKind.PressSurge, 1000, 1000, 17, Side.Home, null, null, 0.7, ["e"], new Dictionary<string, object>());
        Assert.True(gallery.Decide(m, 1060).Air);
        Assert.False(gallery.Decide(m, 1200).Air);
    }
}

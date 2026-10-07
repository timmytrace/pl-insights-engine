using Studio.Crew;
using Studio.Engine;

namespace Studio.Tests;

public class ViewerTests
{
    [Theory]
    [InlineData("analyst.en", Persona.Analyst, "en")]
    [InlineData("casual.es", Persona.Casual, "es")]
    [InlineData("club.fr.away", Persona.ClubFan, "fr")]
    [InlineData("player.en.H10", Persona.PlayerFocus, "en")]
    public void Profiles_parse_from_their_short_form(string spec, Persona persona, string lang)
    {
        var v = ViewerProfile.Parse(spec)!;
        Assert.Equal(persona, v.Persona);
        Assert.Equal(lang, v.Language);
    }

    [Theory]
    [InlineData("analyst.de")]       // unsupported language
    [InlineData("club.en")]          // club fan without a club
    [InlineData("pundit.en")]
    public void Bad_profiles_are_ignored(string spec) => Assert.Null(ViewerProfile.Parse(spec));

    [Fact]
    public void Each_persona_sees_what_matters_to_it()
    {
        Moment M(MomentKind kind, Side team, string? player = null) =>
            new("M", kind, 100, 100, 2, team, player, null, 0.5, ["e"], new Dictionary<string, object>());
        var casual = ViewerProfile.Parse("casual.en")!;
        var homeFan = ViewerProfile.Parse("club.en.home")!;
        var focus = ViewerProfile.Parse("player.en.H10")!;

        Assert.True(Relevance.Shows(casual, M(MomentKind.Goal, Side.Away)));
        Assert.False(Relevance.Shows(casual, M(MomentKind.PressSurge, Side.Home)));      // jargon stays with analysts
        Assert.True(Relevance.Shows(homeFan, M(MomentKind.PressSurge, Side.Home)));
        Assert.False(Relevance.Shows(homeFan, M(MomentKind.ElitePass, Side.Away)));
        Assert.True(Relevance.Shows(homeFan, M(MomentKind.Goal, Side.Away)));             // bad news still airs
        Assert.True(Relevance.Shows(focus, M(MomentKind.SprintSpeed, Side.Home, "H10")));
        Assert.False(Relevance.Shows(focus, M(MomentKind.SprintSpeed, Side.Home, "H07")));
    }
}

public class HostTests
{
    private static readonly IReadOnlyList<ViewerProfile> Viewers =
        ViewerProfile.ParseList("analyst.en,casual.es,club.fr.home,player.en.H10");

    private static async Task<List<CrewResult>> Run(int seed)
    {
        var match = MatchSimulator.Simulate(seed);
        var pipeline = new MatchPipeline(match.Info);
        var crew = new StudioCrew(new ScriptedChatClient(0), new CrewOptions { MaxConcurrentMoments = 100 });
        var results = new List<CrewResult>();
        foreach (var e in match.Events)
        {
            var step = pipeline.Process(e);
            foreach (var m in step.Moments.Where(m => crew.Gallery.Route(m).Route == CrewRoute.Crew))
                results.Add(await crew.RunAsync(crew.Brief(m, pipeline.State), step.Cards.First(c => c.MomentId == m.Id),
                    Viewers, () => m.T, _ => { }, CancellationToken.None));
        }
        return results;
    }

    [Fact]
    public async Task Every_viewer_version_is_verified_and_in_their_language()
    {
        var versions = (await Run(7)).SelectMany(r => r.Versions).ToList();
        Assert.NotEmpty(versions);
        Assert.All(versions, v =>
        {
            Assert.True(v.Verified, string.Join(" ", v.Reasons));
            Assert.Equal(v.Viewer.Id, v.Card.Viewer);
            Assert.Equal(v.Viewer.Language, v.Card.Language);
            Assert.Equal("agent", v.Card.Source);
        });
        Assert.Contains(versions, v => v.Viewer.Language == "es" && v.Card.Headline.Contains("Gol"));
    }

    [Fact]
    public async Task Analysts_see_more_stories_than_casual_fans()
    {
        var versions = (await Run(7)).SelectMany(r => r.Versions).ToList();
        Assert.True(versions.Count(v => v.Viewer.Persona == Persona.Analyst) > versions.Count(v => v.Viewer.Persona == Persona.Casual));
    }

    [Fact]
    public void Ref_reads_decimal_commas_and_foreign_overreach()
    {
        var sheet = new FactSheet("M", "Goal", 22, 100);
        sheet.Add("xg", "xG", 0.89);
        var ok = new List<string>();
        RefChecker.CheckText("Una ocasión de 0,89 xG.", sheet, "es", ok);
        Assert.Empty(ok);

        var wrong = new List<string>();
        RefChecker.CheckText("Une occasion à 0,5 xG, la meilleure de la saison.", sheet, "fr", wrong);
        Assert.Equal(2, wrong.Count);
    }

    [Fact]
    public void Translated_templates_carry_no_english_story_text()
    {
        var match = MatchSimulator.Simulate(7);
        var pipeline = new MatchPipeline(match.Info);
        var goal = match.Events.Select(pipeline.Process).SelectMany(s => s.Cards).First(c => c.Kind == MomentKind.Goal);
        var es = TemplateLocalizer.ForViewer(goal, ViewerProfile.Parse("casual.es")!, match.Info);
        Assert.StartsWith("¡GOL!", es.Headline);
        Assert.Equal("", es.Body);
        Assert.Contains(es.Stats, s => s.Label == "Velocidad del disparo" && s.Value.Contains(',') == goal.Stats.First(x => x.Label == "Shot speed").Value.Contains('.'));
    }
}

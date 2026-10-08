using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Studio.Engine;

namespace Studio.Crew;

public sealed record HostDto(
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("body")] string Body);

/// <summary>One viewer's version of a story, after Ref has checked it.</summary>
public sealed record HostVersion(ViewerProfile Viewer, InsightCard Card, bool Verified, IReadOnlyList<string> Reasons, int NumbersChecked);

/// <summary>
/// The Host: writes each verified story for each viewer, in their language and register.
/// Every version goes back through Ref's rulebook; one that fails falls back to the translated
/// template for that viewer rather than airing anything unchecked.
/// </summary>
public sealed class HostAgent(IChatClient chat)
{
    private readonly AIAgent _agent = StudioCrew.Agent(chat, CrewPrompts.Host, "The Host", "Presenter: personalises and localises verified stories");

    /// <summary>The studio feed's version: the approved pitch as written, in English.</summary>
    public static InsightCard Studio(StoryPitch pitch, InsightCard template) => template with
    {
        Id = template.Id.Replace("-C", "-A"),
        Headline = pitch.Headline,
        Body = pitch.Body,
        Source = "agent",
        Replaces = template.Id,
    };

    public async Task<HostVersion> PresentAsync(StoryPitch pitch, Briefing b, InsightCard template, ViewerProfile viewer,
        CancellationToken ct)
    {
        var info = b.Info;
        var viewerJson = JsonSerializer.Serialize(new
        {
            persona = TemplateLocalizer.PersonaName(viewer.Persona),
            language = viewer.Language,
            languageName = TemplateLocalizer.LanguageName(viewer.Language),
            club = viewer.Club is { } c ? info.Team(c).Name : null,
            storyIsAboutTheirClub = viewer.Club is { } side ? side == b.Moment.Team : (bool?)null,
            player = viewer.PlayerId is { } pid ? TryPlayer(info, pid) : null,
        });

        var fallback = TemplateLocalizer.ForViewer(template, viewer, info);
        var options = new ChatClientAgentRunOptions(new ChatOptions { ResponseFormat = ChatResponseFormat.Json, MaxOutputTokens = 250, Temperature = 0.5f });
        var response = await _agent.RunAsync(CrewPrompts.HostRequest(b.Sheet, pitch, viewerJson), null, options, ct);
        var dto = StudioCrew.Parse<HostDto>(response.Text);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Headline))
            return new HostVersion(viewer, fallback, false, ["The Host's version wasn't valid JSON."], 0);

        var reasons = new List<string>();
        var numbers = RefChecker.CheckText($"{dto.Headline} {dto.Body}", b.Sheet, viewer.Language, reasons);
        if (reasons.Count > 0) return new HostVersion(viewer, fallback, false, reasons, numbers);

        var card = fallback with
        {
            Id = $"{template.Id.Replace("-C", "-A")}-{viewer.Id}",
            Headline = dto.Headline,
            Body = dto.Body,
            Source = "agent",
            Replaces = fallback.Id,
        };
        return new HostVersion(viewer, card, true, [], numbers);
    }

    private static string? TryPlayer(MatchInfo info, string id)
    {
        foreach (var team in new[] { info.Home, info.Away })
            if (team.Starters.Concat(team.Bench).FirstOrDefault(p => p.Id == id) is { } p) return p.Name;
        return null;
    }
}

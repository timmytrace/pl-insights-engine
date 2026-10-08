using System.Diagnostics;

namespace Studio.Crew;

/// <summary>
/// One trace source for the crew. The workflow, every agent and every model call report to it,
/// so a single trace shows a moment travelling Stats → The Gaffer ⇄ Ref → Gallery → The Host,
/// with Ref's verdicts as tags. Exported to Application Insights when the app runs in Azure.
/// </summary>
public static class CrewTelemetry
{
    public const string SourceName = "Studio.Crew";
    public const string ModelSourceName = "Studio.Crew.Model";

    public static readonly ActivitySource Source = new(SourceName);
}

namespace Studio.Crew;

/// <summary>Bound from the "Crew" configuration section.</summary>
public sealed class CrewOptions
{
    /// <summary>"mock" (scripted, offline) or "azure" (Azure OpenAI in Azure AI Foundry).</summary>
    public string Mode { get; set; } = "mock";

    /// <summary>Azure OpenAI resource endpoint, e.g. https://my-resource.openai.azure.com/</summary>
    public string? Endpoint { get; set; }

    /// <summary>Model deployment name, e.g. gpt-4.1-mini.</summary>
    public string? Deployment { get; set; }

    /// <summary>API key. Leave empty to sign in with Microsoft Entra ID (DefaultAzureCredential).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Azure AI Speech region for the spoken recap, e.g. eastus. Empty turns the voices off.</summary>
    public string? SpeechRegion { get; set; }

    /// <summary>Full Azure resource id of the AI resource, used with Entra ID sign-in for Speech.</summary>
    public string? SpeechResourceId { get; set; }

    /// <summary>Speech key, if not signing in with Entra ID.</summary>
    public string? SpeechKey { get; set; }

    public int MaxConcurrentMoments { get; set; } = 3;
    public int MaxRevisions { get; set; } = 2;

    /// <summary>Match seconds after a moment within which an upgraded card may still air.</summary>
    public double FreshnessSeconds { get; set; } = 120;
    public double GoalFreshnessSeconds { get; set; } = 240;

    /// <summary>Simulated model latency in mock mode, so the demo feels like the real thing.</summary>
    public int MockLatencyMs { get; set; } = 350;

    public bool IsAzure => string.Equals(Mode, "azure", StringComparison.OrdinalIgnoreCase);
}

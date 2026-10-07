using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Studio.Crew;

public static class ModelFactory
{
    /// <summary>
    /// The chat model behind the crew. Azure mode talks to Azure OpenAI through its OpenAI-compatible
    /// v1 endpoint, with an API key or Microsoft Entra ID. Mock mode needs nothing and runs offline.
    /// </summary>
    public static IChatClient Create(CrewOptions o)
    {
        if (!o.IsAzure) return new ScriptedChatClient(o.MockLatencyMs);

        if (string.IsNullOrWhiteSpace(o.Endpoint) || string.IsNullOrWhiteSpace(o.Deployment))
            throw new InvalidOperationException("Crew:Mode is 'azure' but Crew:Endpoint or Crew:Deployment is missing.");

        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(new Uri(o.Endpoint), "/openai/v1/") };
        // Keyless Entra ID sign-in is the recommended Azure path; the SDK still flags this constructor as evaluation-only.
#pragma warning disable OPENAI001
        var chat = string.IsNullOrWhiteSpace(o.ApiKey)
            ? new ChatClient(o.Deployment, new BearerTokenPolicy(new DefaultAzureCredential(), "https://cognitiveservices.azure.com/.default"), clientOptions)
            : new ChatClient(o.Deployment, new ApiKeyCredential(o.ApiKey), clientOptions);
#pragma warning restore OPENAI001

        // ChatClientAgent adds function invocation itself, so the raw client is passed through.
        return chat.AsIChatClient();
    }
}

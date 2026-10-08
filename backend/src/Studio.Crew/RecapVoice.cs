using System.Net.Http.Headers;
using System.Security;
using System.Text;
using Azure.Core;
using Azure.Identity;

namespace Studio.Crew;

/// <summary>
/// Gives the crew their voices with Azure AI Speech: the recap script becomes one SSML document
/// with a different neural voice per character, synthesised to MP3 in a single call.
/// Signs in with Microsoft Entra ID by default (needs the Cognitive Services Speech User role),
/// or uses a key if one is configured.
/// </summary>
public sealed class RecapVoice(HttpClient http, CrewOptions options, TokenCredential? credential = null)
{
    private static readonly Dictionary<string, Dictionary<string, string>> Voices = new()
    {
        ["en"] = new() { ["host"] = "en-GB-SoniaNeural", ["gaffer"] = "en-GB-RyanNeural", ["stats"] = "en-US-GuyNeural", ["ref"] = "en-GB-ThomasNeural" },
        ["es"] = new() { ["host"] = "es-ES-ElviraNeural", ["gaffer"] = "es-ES-AlvaroNeural", ["stats"] = "es-MX-JorgeNeural", ["ref"] = "es-MX-DaliaNeural" },
        ["fr"] = new() { ["host"] = "fr-FR-DeniseNeural", ["gaffer"] = "fr-FR-HenriNeural", ["stats"] = "fr-CA-AntoineNeural", ["ref"] = "fr-CA-SylvieNeural" },
    };

    private static readonly Dictionary<string, string> Locales = new() { ["en"] = "en-GB", ["es"] = "es-ES", ["fr"] = "fr-FR" };

    private readonly TokenCredential _credential = credential ?? new DefaultAzureCredential();

    public bool Enabled => options.IsAzure && !string.IsNullOrWhiteSpace(options.SpeechRegion)
                           && (!string.IsNullOrWhiteSpace(options.SpeechKey) || !string.IsNullOrWhiteSpace(options.SpeechResourceId));

    public static string VoiceFor(string language, string speaker) =>
        Voices.GetValueOrDefault(language, Voices["en"]).GetValueOrDefault(speaker, Voices["en"]["host"]);

    public static string Ssml(RecapScript script)
    {
        var sb = new StringBuilder();
        sb.Append($"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{Locales.GetValueOrDefault(script.Language, "en-GB")}\">");
        foreach (var line in script.Lines)
            sb.Append($"<voice name=\"{VoiceFor(script.Language, line.Speaker)}\">{SecurityElement.Escape(line.Text)}<break time=\"350ms\"/></voice>");
        sb.Append("</speak>");
        return sb.ToString();
    }

    /// <summary>MP3 audio for the script, or null when speech isn't configured.</summary>
    public Task<byte[]?> SynthesizeAsync(RecapScript script, CancellationToken ct) =>
        !Enabled || script.Lines.Count == 0 ? Task.FromResult<byte[]?>(null) : PostAsync(Ssml(script), ct);

    /// <summary>One live commentary line in The Host's voice, a little faster and higher the bigger the moment.</summary>
    public Task<byte[]?> SynthesizeLineAsync(string text, string language, int excitement, CancellationToken ct) =>
        !Enabled ? Task.FromResult<byte[]?>(null) : PostAsync(LineSsml(text, language, excitement), ct);

    public static string LineSsml(string text, string language, int excitement)
    {
        var (rate, pitch) = excitement switch { >= 3 => ("+20%", "+12%"), 2 => ("+10%", "+6%"), _ => ("+0%", "+0%") };
        return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{Locales.GetValueOrDefault(language, "en-GB")}\">" +
               $"<voice name=\"{VoiceFor(language, "host")}\"><prosody rate=\"{rate}\" pitch=\"{pitch}\">{SecurityElement.Escape(text)}</prosody></voice></speak>";
    }

    private async Task<byte[]?> PostAsync(string ssml, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://{options.SpeechRegion}.tts.speech.microsoft.com/cognitiveservices/v1")
        {
            Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml"),
        };
        request.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("VirtualStudioCrew", "1.0"));
        if (!string.IsNullOrWhiteSpace(options.SpeechKey))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", options.SpeechKey);
        }
        else
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), ct);
            // Speech expects the resource id alongside an Entra token: "aad#<resource id>#<token>".
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"aad#{options.SpeechResourceId}#{token.Token}");
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Speech returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}

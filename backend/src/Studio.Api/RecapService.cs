using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Studio.Crew;
using Studio.Engine;

namespace Studio.Api;

public sealed record RecapResult(RecapScript Script, byte[]? Audio, string? AudioError);

/// <summary>
/// Builds the full-time recap for a match once per language and caches it: the script costs a
/// model call and the audio a Speech call, and a simulated match never changes.
/// </summary>
public sealed class RecapService(MatchLibrary library, IChatClient chat, RecapVoice voice, UsageGuard usage, ILogger<RecapService> logger)
{
    private readonly ConcurrentDictionary<(int Seed, string Lang), Lazy<Task<RecapResult>>> _cache = new();

    /// <summary>The recap, or null when a new one would go over the hourly budget.</summary>
    public async Task<RecapResult?> GetAsync(int seed, string language)
    {
        var key = (seed, language);
        if (!_cache.ContainsKey(key) && !usage.TryNewRecap()) return null;
        var entry = _cache.GetOrAdd(key, k => new Lazy<Task<RecapResult>>(() => BuildAsync(k.Seed, k.Lang)));
        try
        {
            return await entry.Value;
        }
        catch
        {
            _cache.TryRemove(key, out _);   // let the next request try again
            throw;
        }
    }

    private async Task<RecapResult> BuildAsync(int seed, string language)
    {
        var match = library.Get(seed);
        var pipeline = new MatchPipeline(match.Info);
        var moments = new List<Moment>();
        foreach (var e in match.Events) moments.AddRange(pipeline.Process(e).Moments);

        var sheet = RecapWriter.FullTimeSheet(pipeline.State, moments);
        var script = await new RecapWriter(chat).WriteAsync(sheet, match.Info.MatchId, language, CancellationToken.None);

        byte[]? audio = null;
        string? audioError = null;
        if (!script.Verified)
        {
            audioError = "Ref didn't clear the script, so it isn't read aloud.";
        }
        else if (voice.Enabled)
        {
            try { audio = await voice.SynthesizeAsync(script, CancellationToken.None); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Speech failed for {MatchId} ({Language})", match.Info.MatchId, language);
                audioError = "Speech is unavailable right now.";
            }
        }
        return new RecapResult(script, audio, audioError);
    }
}

using System.Collections.Concurrent;
using Studio.Crew;
using Studio.Engine;

namespace Studio.Api;

/// <summary>
/// Live commentary for a match, written once per language and cached (it's deterministic).
/// Audio is only ever produced for lines the server wrote itself and marked as voiced, so the
/// public endpoint can't be used to synthesise arbitrary text.
/// </summary>
public sealed class CommentaryService(MatchLibrary library, RecapVoice voice, UsageGuard usage, ILogger<CommentaryService> logger)
{
    private readonly ConcurrentDictionary<(int, string), IReadOnlyDictionary<int, CommentaryLine>> _lines = new();
    private readonly ConcurrentDictionary<(int, string, int), Lazy<Task<byte[]?>>> _audio = new();

    public IReadOnlyDictionary<int, CommentaryLine> Lines(int seed, string language) =>
        _lines.GetOrAdd((seed, language), key =>
        {
            var match = library.Get(key.Item1);
            var pipeline = new MatchPipeline(match.Info);
            var commentator = new Commentator(match.Info, key.Item2);
            var lines = new Dictionary<int, CommentaryLine>();
            foreach (var e in match.Events)
            {
                pipeline.Process(e);
                if (commentator.Call(e, pipeline.State) is { } line) lines[e.Seq] = line;
            }
            return lines;
        });

    public bool VoiceAvailable => voice.Enabled;

    public async Task<byte[]?> AudioAsync(int seed, string language, int seq)
    {
        if (!voice.Enabled || !Lines(seed, language).TryGetValue(seq, out var line) || !line.Voiced) return null;
        var entry = _audio.GetOrAdd((seed, language, seq), _ => new Lazy<Task<byte[]?>>(async () =>
        {
            if (!usage.TrySpeech()) return null;   // over budget: captions carry on without voice
            try { return await voice.SynthesizeLineAsync(line.Text, language, line.Excitement, CancellationToken.None); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Commentary speech failed for {Seq}", seq);
                return null;
            }
        }));
        var audio = await entry.Value;
        if (audio is null) _audio.TryRemove((seed, language, seq), out _);   // allow a retry later
        return audio;
    }
}

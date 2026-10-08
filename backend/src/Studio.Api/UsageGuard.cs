using System.Collections.Concurrent;

namespace Studio.Api;

/// <summary>
/// Keeps a public demo from spending the Azure budget: caps how many live-crew replays run at
/// once and how many recaps and voiced lines are generated per hour. Over the limit, the app
/// degrades gracefully (template cards, captions without voice) rather than failing.
/// </summary>
public sealed class UsageGuard(IConfiguration config)
{
    private readonly int _maxCrewReplays = config.GetValue("Usage:MaxConcurrentCrewReplays", 3);
    private readonly int _speechPerHour = config.GetValue("Usage:SpeechCallsPerHour", 400);
    private readonly int _recapsPerHour = config.GetValue("Usage:NewRecapsPerHour", 30);
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();
    private int _crewReplays;

    /// <summary>Claim a live-crew slot; dispose the result to release it. Null when all slots are taken.</summary>
    public IDisposable? TryStartCrewReplay()
    {
        if (Interlocked.Increment(ref _crewReplays) <= _maxCrewReplays) return new Release(() => Interlocked.Decrement(ref _crewReplays));
        Interlocked.Decrement(ref _crewReplays);
        return null;
    }

    public bool TrySpeech() => TryHourly("speech", _speechPerHour);

    public bool TryNewRecap() => TryHourly("recap", _recapsPerHour);

    private bool TryHourly(string bucket, int limit)
    {
        var q = _windows.GetOrAdd(bucket, _ => new Queue<DateTime>());
        lock (q)
        {
            var now = DateTime.UtcNow;
            while (q.Count > 0 && now - q.Peek() > TimeSpan.FromHours(1)) q.Dequeue();
            if (q.Count >= limit) return false;
            q.Enqueue(now);
            return true;
        }
    }

    private sealed class Release(Action onDispose) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) onDispose(); }
    }
}

using Studio.Engine;

namespace Studio.Api;

/// <summary>One message on the replay socket. Type is info | event | snapshot | moment | card | end.</summary>
public sealed record Envelope(string Type, double T, object Data);

/// <summary>
/// Replays a simulated match through the pipeline in (scaled) real time, emitting each event,
/// the moments and cards it triggers, and a stats snapshot every few seconds of match time.
/// </summary>
public sealed class ReplayStreamer(Match match, double speed)
{
    private const double SnapshotEvery = 5;       // match seconds
    private const double MaxSleepSeconds = 2;     // dead-ball gaps don't stall the stream

    public async Task StreamAsync(Func<Envelope, CancellationToken, Task> send, CancellationToken ct)
    {
        var pipeline = new MatchPipeline(match.Info);
        await send(new Envelope("info", 0, match.Info), ct);

        double previousT = 0, lastSnapshot = double.NegativeInfinity;
        foreach (var e in match.Events)
        {
            var wait = Math.Min((e.T - previousT) / speed, MaxSleepSeconds);
            if (wait > 0.001) await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            previousT = e.T;

            var step = pipeline.Process(e);
            await send(new Envelope("event", e.T, step.Metrics), ct);
            foreach (var m in step.Moments) await send(new Envelope("moment", e.T, m), ct);
            foreach (var c in step.Cards) await send(new Envelope("card", e.T, c), ct);

            var goal = e is { Type: EventType.Shot, Outcome: "goal" };
            if (goal || e.Type == EventType.PeriodEnd || e.T - lastSnapshot >= SnapshotEvery)
            {
                lastSnapshot = e.T;
                await send(new Envelope("snapshot", e.T, pipeline.State.Snapshot()), ct);
            }
        }
        await send(new Envelope("end", previousT, pipeline.State.Snapshot()), ct);
    }
}

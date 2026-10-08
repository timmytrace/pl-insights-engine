using System.Threading.Channels;
using Studio.Crew;
using Studio.Engine;

namespace Studio.Api;

/// <summary>One message on the replay socket. Type is info | notice | event | commentary | snapshot | moment | card | crew | end.</summary>
public sealed record Envelope(string Type, double T, object Data);

/// <summary>
/// Replays a simulated match through the pipeline in (scaled) real time. Each moment's template
/// card goes out immediately; if Gallery sends the moment to the crew, the crew works on it in
/// the background and its Control Room messages and upgraded card join the stream when ready.
/// </summary>
public sealed class ReplayStreamer(Match match, double speed, StudioCrew? crew, IReadOnlyList<ViewerProfile> viewers,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, CommentaryLine>>? commentary = null, string? notice = null,
    ILogger? logger = null)
{
    private const double SnapshotEvery = 5;       // match seconds
    private const double MaxSleepSeconds = 2;     // dead-ball gaps don't stall the stream
    private static readonly TimeSpan CrewDrainTimeout = TimeSpan.FromSeconds(20);

    private readonly Channel<Envelope> _outbox = Channel.CreateUnbounded<Envelope>();
    private readonly List<Task> _crewWork = [];
    private double _now;

    public async Task StreamAsync(Func<Envelope, CancellationToken, Task> send, CancellationToken ct)
    {
        var pipeline = new MatchPipeline(match.Info);
        await send(new Envelope("info", 0, match.Info), ct);
        if (notice is not null) await send(new Envelope("notice", 0, new { text = notice }), ct);

        double previousT = 0, lastSnapshot = double.NegativeInfinity;
        foreach (var e in match.Events)
        {
            var wait = Math.Min((e.T - previousT) / speed, MaxSleepSeconds);
            await WaitAndDrain(TimeSpan.FromSeconds(Math.Max(wait, 0)), send, ct);
            previousT = e.T;
            Volatile.Write(ref _now, e.T);

            var step = pipeline.Process(e);
            await send(new Envelope("event", e.T, step.Metrics), ct);
            foreach (var lines in commentary?.Values ?? [])
                if (lines.TryGetValue(e.Seq, out var line)) await send(new Envelope("commentary", e.T, line), ct);
            foreach (var m in step.Moments)
            {
                await send(new Envelope("moment", e.T, m), ct);
                var template = step.Cards.First(c => c.MomentId == m.Id);
                await send(new Envelope("card", e.T, template), ct);
                foreach (var v in viewers.Where(v => Relevance.Shows(v, m)))
                    await send(new Envelope("card", e.T, TemplateLocalizer.ForViewer(template, v, match.Info)), ct);
                if (crew is not null) await Dispatch(m, template, pipeline.State, send, ct);
            }

            var goal = e is { Type: EventType.Shot, Outcome: "goal" };
            if (goal || e.Type == EventType.PeriodEnd || e.T - lastSnapshot >= SnapshotEvery)
            {
                lastSnapshot = e.T;
                await send(new Envelope("snapshot", e.T, pipeline.State.Snapshot()), ct);
            }
        }

        // Let the crew finish what it started, then close.
        var pending = Task.WhenAll(_crewWork);
        await Task.WhenAny(pending, Task.Delay(CrewDrainTimeout, ct));
        _outbox.Writer.TryComplete();
        await WaitAndDrain(TimeSpan.Zero, send, ct);
        await send(new Envelope("end", previousT, pipeline.State.Snapshot()), ct);
    }

    private async Task Dispatch(Moment m, InsightCard template, MatchState state,
        Func<Envelope, CancellationToken, Task> send, CancellationToken ct)
    {
        var route = crew!.Gallery.Route(m);
        await send(new Envelope("crew", m.T, new CrewMessage(m.Id, CrewRole.Gallery, CrewMessageKind.Decision, route.Reason, m.T,
            new Dictionary<string, object> { ["route"] = route.Route.ToString().ToLowerInvariant() })), ct);
        if (route.Route != CrewRoute.Crew) return;

        // Brief now, on the pipeline thread, while the match state is exactly at this moment.
        var briefing = crew.Brief(m, state);
        _crewWork.Add(Task.Run(async () =>
        {
            try
            {
                var result = await crew.RunAsync(briefing, template, viewers, () => Volatile.Read(ref _now),
                    msg => _outbox.Writer.TryWrite(new Envelope("crew", msg.MatchT, msg)), ct);
                if (result.Card is { } card) _outbox.Writer.TryWrite(new Envelope("card", Volatile.Read(ref _now), card));
                foreach (var version in result.Versions.Where(v => v.Verified))
                    _outbox.Writer.TryWrite(new Envelope("card", Volatile.Read(ref _now), version.Card));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Crew failed on {MomentId}", m.Id);
                _outbox.Writer.TryWrite(new Envelope("crew", Volatile.Read(ref _now), new CrewMessage(m.Id, CrewRole.Gallery,
                    CrewMessageKind.Decision, "Lost the crew on this one. The template graphic stays on air.", Volatile.Read(ref _now))));
            }
        }, ct));
    }

    /// <summary>Wait for the next event while forwarding anything the crew finishes in the meantime.</summary>
    private async Task WaitAndDrain(TimeSpan wait, Func<Envelope, CancellationToken, Task> send, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            while (_outbox.Reader.TryRead(out var env)) await send(env, ct);
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return;
            var ready = _outbox.Reader.WaitToReadAsync(ct).AsTask();
            if (await Task.WhenAny(ready, Task.Delay(remaining, ct)) != ready) return;
            if (!await ready) return;
        }
    }
}

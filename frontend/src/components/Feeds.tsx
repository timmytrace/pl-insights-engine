import type { EventMetrics, InsightCard, MatchInfo } from '../types'
import { playerName, teamColour } from '../format'

export function EventTicker({ info, feed }: { info?: MatchInfo; feed: EventMetrics[] }) {
  return (
    <section className="panel feed">
      <h2>Event feed</h2>
      <ol className="feed-list">
        {feed.map(({ event: e }) => (
          <li key={e.id} style={{ '--team': teamColour(info, e.team) } as React.CSSProperties}>
            <span className="feed-min">{Math.floor(e.clock / 60) + 1}'</span>
            <span className="feed-type">{e.type.replace('_', ' ')}</span>
            <span className="feed-text">{describe(info, e)}</span>
          </li>
        ))}
      </ol>
    </section>
  )
}

function describe(info: MatchInfo | undefined, e: EventMetrics['event']): string {
  const who = playerName(info, e.playerId)
  switch (e.type) {
    case 'shot': return `${who} · ${e.outcome?.replace('_', ' ')} · xG ${e.xg?.toFixed(2)} · ${e.ballSpeedKmh?.toFixed(0)} km/h`
    case 'substitution': return `${who} on for ${playerName(info, e.subOffId)}`
    case 'period_end': return e.outcome === 'end_of_period_1' ? 'Half-time' : 'Full-time'
    case 'kickoff': return `${who} kicks off`
    default: return who
  }
}

export function CardLog({ cards }: { cards: InsightCard[] }) {
  return (
    <section className="panel card-log">
      <h2>Insight cards <span className="muted">{cards.length}</span></h2>
      <ol>
        {cards.map((c) => (
          <li key={c.id} className={`prio-${c.priority}`}>
            <div className="card-log-head">
              <span className="feed-min">{c.minute}'</span>
              <span className="tag">{c.kind.replace('_', ' ')}</span>
              <span className="muted">{c.source}</span>
            </div>
            <strong>{c.headline}</strong>
            {c.body && <p>{c.body}</p>}
          </li>
        ))}
      </ol>
    </section>
  )
}

import type { EventMetrics, MatchInfo } from '../types'
import { teamColour } from '../format'

interface Props {
  info?: MatchInfo
  recent: EventMetrics[]
  /** Player-focus mode: this player's actions are drawn bolder and ringed. */
  highlightPlayer?: string
  children?: React.ReactNode   // overlay layer, positioned over the pitch like graphics over a feed
}

/** A 105 x 68 m pitch drawn in metres, so event coordinates plot directly. */
export function Pitch({ info, recent, highlightPlayer, children }: Props) {
  const last = recent.at(-1)?.event
  const ball = last ? { x: last.endX ?? last.x, y: last.endY ?? last.y } : undefined

  return (
    <div className="pitch-frame">
      <svg viewBox="-2 -2 109 72" className="pitch" role="img" aria-label="Live pitch view">
        <rect x="-2" y="-2" width="109" height="72" className="pitch-grass" />
        {Array.from({ length: 7 }, (_, i) => (
          <rect key={i} x={i * 15} y="0" width="7.5" height="68" className="pitch-stripe" />
        ))}
        <g className="pitch-lines">
          <rect x="0" y="0" width="105" height="68" />
          <line x1="52.5" y1="0" x2="52.5" y2="68" />
          <circle cx="52.5" cy="34" r="9.15" />
          <circle cx="52.5" cy="34" r="0.4" className="spot" />
          <rect x="0" y="13.84" width="16.5" height="40.32" />
          <rect x="88.5" y="13.84" width="16.5" height="40.32" />
          <rect x="0" y="24.84" width="5.5" height="18.32" />
          <rect x="99.5" y="24.84" width="5.5" height="18.32" />
          <rect x="-1.5" y="30.34" width="1.5" height="7.32" />
          <rect x="105" y="30.34" width="1.5" height="7.32" />
          <circle cx="11" cy="34" r="0.4" className="spot" />
          <circle cx="94" cy="34" r="0.4" className="spot" />
        </g>

        {recent.map((m, i) => {
          const e = m.event
          if (e.x == null || e.y == null || e.endX == null || e.endY == null) return null
          const opacity = 0.25 + (0.75 * (i + 1)) / recent.length
          const colour = teamColour(info, e.team)
          if (e.type === 'pass') {
            return (
              <line key={e.id} x1={e.x} y1={e.y} x2={e.endX} y2={e.endY} stroke={colour}
                strokeWidth={0.5} strokeOpacity={opacity} strokeDasharray={e.outcome === 'complete' ? undefined : '1.2 0.8'}
                markerEnd="url(#arrow)" />
            )
          }
          if (e.type === 'carry') {
            return <line key={e.id} x1={e.x} y1={e.y} x2={e.endX} y2={e.endY} stroke={colour}
              strokeWidth={0.4} strokeOpacity={opacity} strokeDasharray="0.4 0.6" />
          }
          if (e.type === 'shot') {
            return <line key={e.id} x1={e.x} y1={e.y} x2={e.endX} y2={e.endY} stroke="#ffd166"
              strokeWidth={0.7} strokeOpacity={opacity} />
          }
          return null
        })}

        {recent.filter((m) => m.event.type === 'pressure' || m.event.type === 'tackle' || m.event.type === 'interception').map((m) =>
          m.event.x != null && m.event.y != null ? (
            <circle key={m.event.id} cx={m.event.x} cy={m.event.y} r={1.6} fill="none"
              stroke={teamColour(info, m.event.team)} strokeWidth={0.35} className="pulse" />
          ) : null,
        )}

        {highlightPlayer && recent.filter((m) => m.event.playerId === highlightPlayer && m.event.x != null).map((m) => (
          <circle key={`f-${m.event.id}`} cx={m.event.x} cy={m.event.y} r={1.8} className="focus-ring" />
        ))}

        {ball?.x != null && ball.y != null && (
          <circle cx={ball.x} cy={ball.y} r={0.9} className="ball" />
        )}

        <defs>
          <marker id="arrow" viewBox="0 0 6 6" refX="5" refY="3" markerWidth="4" markerHeight="4" orient="auto-start-reverse">
            <path d="M0,0 L6,3 L0,6 z" fill="context-stroke" />
          </marker>
        </defs>
      </svg>
      {children}
    </div>
  )
}

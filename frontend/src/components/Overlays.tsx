import type { ActiveCard } from '../useReplay'
import type { InsightCard, MatchInfo, Slot } from '../types'
import { teamColour } from '../format'

interface Props {
  info?: MatchInfo
  overlays: Partial<Record<Slot, ActiveCard>>
}

/** Broadcast-style graphics. Each slot is a fixed screen position, driven only by card data. */
export function Overlays({ info, overlays }: Props) {
  return (
    <div className="overlays" aria-live="polite">
      {(['banner', 'corner', 'player_tag', 'lower_third'] as Slot[]).map((slot) => {
        const active = overlays[slot]
        return active ? <OverlayCard key={active.card.id} card={active.card} colour={teamColour(info, active.card.team)} /> : null
      })}
    </div>
  )
}

function OverlayCard({ card, colour }: { card: InsightCard; colour: string }) {
  return (
    <div className={`overlay overlay-${card.slot.replace('_', '-')}`} style={{ '--team': colour } as React.CSSProperties}>
      <div className="overlay-headline">{card.headline}</div>
      {card.source === 'agent' && <div className="overlay-verified">✓ Verified by Ref</div>}
      {card.body && <div className="overlay-body">{card.body}</div>}
      {card.stats.length > 0 && (
        <div className="overlay-stats">
          {card.stats.map((s) => (
            <div key={s.label} className="overlay-stat">
              <span className="overlay-stat-value">{s.value}<small>{s.unit}</small></span>
              <span className="overlay-stat-label">{s.label}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

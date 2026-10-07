import type { ActiveCard } from '../useReplay'
import type { InsightCard, MatchInfo, Slot } from '../types'
import { teamColour } from '../format'

interface Props {
  info?: MatchInfo
  overlays: Partial<Record<Slot, ActiveCard>>
  /** Small panes show only the most important graphic, so nothing overlaps. */
  single?: boolean
}

/** Broadcast-style graphics. Each slot is a fixed screen position, driven only by card data. */
export function Overlays({ info, overlays, single }: Props) {
  let active = (['banner', 'corner', 'player_tag', 'lower_third'] as Slot[])
    .map((slot) => overlays[slot])
    .filter((a): a is ActiveCard => !!a)
  if (single && active.length > 1) {
    active = [active.reduce((best, a) =>
      a.card.priority < best.card.priority || (a.card.priority === best.card.priority && a.card.showAtT > best.card.showAtT) ? a : best)]
  }
  return (
    <div className="overlays" aria-live="polite">
      {active.map((a) => <OverlayCard key={a.card.id} card={a.card} colour={teamColour(info, a.card.team)} />)}
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

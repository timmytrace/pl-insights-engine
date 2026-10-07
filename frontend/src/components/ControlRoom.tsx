import { useEffect, useMemo, useRef } from 'react'
import type { CrewMessage, Moment } from '../types'
import type { CrewTally } from '../useReplay'
import { crewMember } from '../crew'
import { Avatar } from './Avatar'
import { clipState } from '../clips'

interface Props {
  messages: CrewMessage[]
  moments: Record<string, Moment>
  tally: CrewTally
  onWhy?: (momentId: string) => void
}

const TAGS = new Set(['shot_speed', 'sprint_speed', 'milestone'])

const KIND_LABEL: Record<string, string> = {
  goal: 'Goal', big_chance: 'Big chance', press_surge: 'Pressing surge', momentum_swing: 'Momentum swing',
  chaos_spell: 'Chaos', control_spell: 'Control', elite_pass: 'Standout pass', shot_speed: 'Shot speed',
  sprint_speed: 'Sprint', milestone: 'Milestone',
}

/** The crew at work: every brief, pitch, verdict and decision, threaded by moment. */
export function ControlRoom({ messages, moments, tally, onWhy }: Props) {
  const threads = useMemo(() => {
    const byMoment = new Map<string, CrewMessage[]>()
    for (const m of messages) {
      // Tags Gallery sends straight to air are noise in the transcript; keep the stories.
      const kind = moments[m.momentId]?.kind
      if (kind && TAGS.has(kind)) continue
      byMoment.set(m.momentId, [...(byMoment.get(m.momentId) ?? []), m])
    }
    return [...byMoment.entries()].slice(-12)
  }, [messages, moments])

  const scroller = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const el = scroller.current
    if (el) el.scrollTop = el.scrollHeight
  }, [messages.length])

  return (
    <section className="panel control-room">
      <h2>Control Room <span className="muted">every claim checked before air</span></h2>
      <div className="tally">
        <Tile value={tally.claimsChecked} label="claims checked" />
        <Tile value={tally.numbersChecked} label="numbers checked" />
        <Tile value={tally.sentBack} label="sent back by Ref" tone="red" />
        <Tile value={tally.upgraded} label="verified on air" tone="green" />
      </div>
      <div className="threads" ref={scroller}>
        {threads.length === 0 && <p className="muted">The crew is waiting for the first moment…</p>}
        {threads.map(([momentId, msgs]) => {
          const moment = moments[momentId]
          return (
            <article key={momentId} className="thread">
              <header>
                <span className="feed-min">{moment ? `${moment.minute}'` : ''}</span>
                <span className="tag">{moment ? KIND_LABEL[moment.kind] ?? moment.kind : 'Moment'}</span>
                {moment?.playerName && <span className="muted">{moment.playerName}</span>}
                {onWhy && moment && <button type="button" className="why-link" onClick={() => onWhy(momentId)}>Why?</button>}
              </header>
              {msgs.map((m, i) => <Line key={i} m={m} live={m === messages[messages.length - 1]} />)}
            </article>
          )
        })}
      </div>
    </section>
  )
}

function Line({ m, live }: { m: CrewMessage; live: boolean }) {
  const who = crewMember(m.from)
  const rejected = m.kind === 'verdict' && m.data?.approved === false
  const approved = m.kind === 'verdict' && m.data?.approved === true
  const dropped = m.kind === 'decision' && m.data?.air === false
  return (
    <div className={`line line-${m.kind}${rejected ? ' line-rejected' : ''}`} style={{ '--crew': who.colour } as React.CSSProperties}>
      <Avatar id={who.id} size={28} state={live ? clipState(m.kind, m.data?.approved) : undefined} />
      <div>
        <strong>{who.name}</strong>
        {rejected && <span className="chip chip-red">Sent back</span>}
        {approved && <span className="chip chip-green">{m.data?.stage === 'host' ? 'All versions verified' : 'Verified'}</span>}
        {dropped && <span className="chip">Dropped</span>}
        {m.kind === 'on_air' && <span className="chip chip-live">On air</span>}
        {m.kind === 'pitch' && (m.data?.round ?? 1) > 1 && <span className="chip">Revision {m.data!.round! - 1}</span>}
        <p>{m.text}</p>
      </div>
    </div>
  )
}

function Tile({ value, label, tone }: { value: number; label: string; tone?: 'red' | 'green' }) {
  return (
    <div className={`tally-tile${tone ? ` tally-${tone}` : ''}`}>
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  )
}

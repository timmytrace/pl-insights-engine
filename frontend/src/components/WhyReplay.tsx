import { useEffect, useState } from 'react'
import type { InsightCard, MatchEvent, MatchInfo, Moment } from '../types'
import { describeEvent } from '../format'
import { Pitch } from './Pitch'
import { Avatar } from './Avatar'

interface Props {
  info?: MatchInfo
  moment: Moment
  events: Record<string, MatchEvent>
  story?: InsightCard
  onClose: () => void
}

const STEP_MS = 1100

/**
 * "Why did that happen?" Rewinds the events that led to a moment, one at a time, then shows
 * the verified story. The captions come straight from the event data; the story is the one
 * Ref approved, so every step and every word traces back to evidence.
 */
export function WhyReplay({ info, moment, events, story, onClose }: Props) {
  const chain = moment.evidence.map((id) => events[id]).filter((e): e is MatchEvent => !!e)
  const [step, setStep] = useState(0)
  const [run, setRun] = useState(0)

  useEffect(() => {
    if (step >= chain.length) return
    const id = setTimeout(() => setStep((s) => s + 1), STEP_MS)
    return () => clearTimeout(id)
  }, [step, chain.length, run])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const shown = chain.slice(0, Math.max(step, 1)).map((event) => ({ event, progressive: false, keyPassCredit: false }))
  const done = step >= chain.length

  return (
    <div className="why-backdrop" role="dialog" aria-modal="true" aria-label="Why did that happen?" onClick={onClose}>
      <div className="why" onClick={(e) => e.stopPropagation()}>
        <header className="why-head">
          <div>
            <h3>Why did that happen?</h3>
            <span className="muted">{moment.minute}' · {moment.kind.replace('_', ' ')}{moment.playerName ? ` · ${moment.playerName}` : ''}</span>
          </div>
          <div className="why-actions">
            <button type="button" onClick={() => { setStep(0); setRun((r) => r + 1) }}>Replay</button>
            <button type="button" onClick={onClose} aria-label="Close">✕</button>
          </div>
        </header>
        <div className="why-body">
          <Pitch info={info} recent={shown} />
          <ol className="why-steps">
            {chain.map((e, i) => (
              <li key={e.id} className={i < step ? 'done' : i === step ? 'next' : ''}>
                <span className="feed-min">{Math.floor(e.clock / 60) + 1}'</span>
                {describeEvent(info, e)}
              </li>
            ))}
          </ol>
        </div>
        {done && story && (
          <footer className="why-story">
            <Avatar id="host" size={40} state="on_air" />
            <div>
              <strong>{story.headline}</strong>
              <p>{story.body}</p>
              {story.source === 'agent' && <span className="overlay-verified">✓ Verified by Ref against {moment.evidence.length} events and the fact sheet</span>}
            </div>
          </footer>
        )}
      </div>
    </div>
  )
}

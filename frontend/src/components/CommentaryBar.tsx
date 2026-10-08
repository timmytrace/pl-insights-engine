import type { CommentaryLine } from '../types'

/** Live play-by-play under the pitch: the latest call, with the previous one fading out above it. */
export function CommentaryBar({ lines }: { lines?: CommentaryLine[] }) {
  const latest = lines?.at(-1)
  const previous = lines?.at(-2)
  return (
    <div className="commentary" aria-live="polite" aria-label="Live commentary">
      <span className="commentary-mic" aria-hidden>🎙</span>
      <div className="commentary-lines">
        {previous && <p key={previous.eventId} className="commentary-prev">{previous.minute}' {previous.text}</p>}
        {latest
          ? <p key={latest.eventId} className={`commentary-now excite-${latest.excitement}`}>{latest.minute}' {latest.text}</p>
          : <p className="commentary-now muted">Commentary starts at kick-off.</p>}
      </div>
    </div>
  )
}

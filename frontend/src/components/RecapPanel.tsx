import { useEffect, useRef, useState } from 'react'
import type { CrewId } from '../crew'
import { crewMember } from '../crew'
import { LANGS, type Lang } from '../viewers'
import { Avatar } from './Avatar'

interface RecapLine { speaker: CrewId; text: string }
interface Recap {
  script: { language: Lang; lines: RecapLine[]; verified: boolean; numbersChecked: number; reasons: string[] }
  hasAudio: boolean
  audioError?: string
}

interface Props { seed: number; onClose: () => void }

/**
 * The full-time recap: a short studio conversation, checked by Ref, read by the crew's Azure
 * neural voices. Without audio (offline mode) the browser's own speech reads it instead.
 */
export function RecapPanel({ seed, onClose }: Props) {
  const [lang, setLang] = useState<Lang>('en')
  const [recap, setRecap] = useState<Recap>()
  const [error, setError] = useState<string>()
  const [current, setCurrent] = useState(-1)
  const audio = useRef<HTMLAudioElement>(null)

  useEffect(() => {
    const ctrl = new AbortController()
    setRecap(undefined)
    setError(undefined)
    setCurrent(-1)
    fetch(`/api/matches/${seed}/recap?lang=${lang}`, { signal: ctrl.signal })
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
      .then(setRecap)
      .catch((e: Error) => { if (e.name !== 'AbortError') setError('The crew couldn\'t write the recap. Try again in a moment.') })
    return () => { ctrl.abort(); speechSynthesis.cancel() }
  }, [seed, lang])

  const lines = recap?.script.lines ?? []

  // With one audio file for the whole conversation, estimate the current line from how far
  // through the text the playback is.
  const onTime = () => {
    const el = audio.current
    if (!el || !el.duration || lines.length === 0) return
    const lengths = lines.map((l) => l.text.length + 12)
    const total = lengths.reduce((a, b) => a + b, 0)
    let at = (el.currentTime / el.duration) * total
    let i = 0
    while (i < lengths.length - 1 && at > lengths[i]) { at -= lengths[i]; i++ }
    setCurrent(i)
  }

  const speakInBrowser = () => {
    speechSynthesis.cancel()
    const voices = speechSynthesis.getVoices().filter((v) => v.lang.startsWith(lang))
    lines.forEach((line, i) => {
      const u = new SpeechSynthesisUtterance(line.text)
      u.lang = lang
      if (voices.length) u.voice = voices[['host', 'gaffer', 'stats', 'ref'].indexOf(line.speaker) % voices.length]
      u.pitch = line.speaker === 'gaffer' ? 0.85 : line.speaker === 'stats' ? 1.1 : 1
      u.onstart = () => setCurrent(i)
      u.onend = () => { if (i === lines.length - 1) setCurrent(-1) }
      speechSynthesis.speak(u)
    })
  }

  return (
    <div className="why-backdrop" role="dialog" aria-modal="true" aria-label="Full-time recap" onClick={onClose}>
      <div className="why recap" onClick={(e) => e.stopPropagation()}>
        <header className="why-head">
          <div>
            <h3>Full-time recap</h3>
            <span className="muted">The crew talks you through the match</span>
          </div>
          <div className="why-actions">
            <select value={lang} onChange={(e) => setLang(e.target.value as Lang)} aria-label="Recap language">
              {LANGS.map((l) => <option key={l.id} value={l.id}>{l.label}</option>)}
            </select>
            <button type="button" onClick={onClose} aria-label="Close">✕</button>
          </div>
        </header>

        {!recap && !error && <p className="muted">The Host is writing the recap and Ref is checking it…</p>}
        {error && <p className="muted">{error}</p>}

        {recap && (
          <>
            <div className="recap-player">
              {recap.hasAudio ? (
                <audio ref={audio} controls autoPlay src={`/api/matches/${seed}/recap/audio?lang=${lang}`}
                  onTimeUpdate={onTime} onEnded={() => setCurrent(-1)} />
              ) : (
                <button type="button" className="primary" onClick={speakInBrowser}>▶ Play with browser voices</button>
              )}
              <span className={recap.script.verified ? 'overlay-verified' : 'muted'}>
                {recap.script.verified
                  ? `✓ Verified by Ref: ${recap.script.numbersChecked} numbers checked`
                  : `Not verified: ${recap.script.reasons.join(' ')}`}
              </span>
            </div>
            <ol className="recap-lines">
              {lines.map((line, i) => {
                const who = crewMember(line.speaker)
                return (
                  <li key={i} className={i === current ? 'speaking' : ''} style={{ '--crew': who.colour } as React.CSSProperties}>
                    <Avatar id={who.id} size={36} state={i === current ? 'on_air' : undefined} />
                    <div>
                      <strong>{who.name}</strong>
                      <p>{line.text}</p>
                    </div>
                  </li>
                )
              })}
            </ol>
          </>
        )}
      </div>
    </div>
  )
}

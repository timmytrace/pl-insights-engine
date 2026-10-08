import type { InsightCard, MatchInfo, MatchSnapshot, Moment } from '../types'
import type { ReplayState } from '../useReplay'
import { LANGS, PERSONAS, personaLabel, spec, type Viewer } from '../viewers'
import { Pitch } from './Pitch'
import { Overlays } from './Overlays'
import { Avatar } from './Avatar'
import { CommentaryBar } from './CommentaryBar'

interface Props {
  state: ReplayState
  viewers: Viewer[]
  /** Match info for the selected seed, available before kick-off for the pickers. */
  preview?: MatchInfo
  running: boolean
  onChange: (viewers: Viewer[]) => void
  onWhy: (moment: Moment, story: InsightCard) => void
}

/** The same match, made for each viewer: one pane per profile, side by side. */
export function FanView({ state, viewers, preview, running, onChange, onWhy }: Props) {
  const info = state.info ?? preview
  const update = (i: number, v: Viewer) => onChange(viewers.map((x, j) => (j === i ? v : x)))

  return (
    <section className="fan-view">
      <div className={`fan-grid fan-grid-${viewers.length}`}>
        {viewers.map((v, i) => (
          <FanPane key={i} viewer={v} info={info} state={state} running={running}
            onChange={(nv) => update(i, nv)}
            onRemove={viewers.length > 1 ? () => onChange(viewers.filter((_, j) => j !== i)) : undefined}
            onWhy={onWhy} />
        ))}
      </div>
      {viewers.length < 4 && (
        <button type="button" className="add-viewer" disabled={running}
          onClick={() => onChange([...viewers, { persona: 'club', lang: 'en', club: 'home' }])}>
          + Add a viewer
        </button>
      )}
      {running && <p className="muted fan-hint">Viewer changes apply at the next kick-off.</p>}
    </section>
  )
}

interface PaneProps {
  viewer: Viewer
  info?: MatchInfo
  state: ReplayState
  running: boolean
  onChange: (v: Viewer) => void
  onRemove?: () => void
  onWhy: (moment: Moment, story: InsightCard) => void
}

function FanPane({ viewer, info, state, running, onChange, onRemove, onWhy }: PaneProps) {
  const key = spec(viewer)
  const stories = state.stories[key] ?? []
  const players = info ? [...info.home.starters.map((p) => ({ ...p, team: info.home.shortName })), ...info.away.starters.map((p) => ({ ...p, team: info.away.shortName }))] : []
  const subject = viewer.persona === 'club' && info ? info[viewer.club ?? 'home'].name
    : viewer.persona === 'player' ? players.find((p) => p.id === viewer.player)?.name : undefined

  return (
    <article className="fan-pane">
      <header className="fan-head">
        <Avatar id="host" size={44} state={stories[0]?.source === 'agent' ? 'on_air' : undefined} />
        <div className="fan-title">
          <strong>{personaLabel(viewer.persona)}{subject ? ` · ${subject}` : ''}</strong>
          <span className="muted">The Host · {LANGS.find((l) => l.id === viewer.lang)!.label}</span>
        </div>
        {onRemove && !running && <button type="button" className="icon" onClick={onRemove} aria-label="Remove viewer">✕</button>}
      </header>

      <fieldset className="fan-settings" disabled={running}>
        <select value={viewer.persona} aria-label="Persona" onChange={(e) => onChange({ ...viewer, persona: e.target.value as Viewer['persona'] })}>
          {PERSONAS.map((p) => <option key={p.id} value={p.id}>{p.label}</option>)}
        </select>
        <select value={viewer.lang} aria-label="Language" onChange={(e) => onChange({ ...viewer, lang: e.target.value as Viewer['lang'] })}>
          {LANGS.map((l) => <option key={l.id} value={l.id}>{l.label}</option>)}
        </select>
        {viewer.persona === 'club' && (
          <select value={viewer.club ?? 'home'} aria-label="Club" onChange={(e) => onChange({ ...viewer, club: e.target.value as 'home' | 'away' })}>
            <option value="home">{info?.home.name ?? 'Home'}</option>
            <option value="away">{info?.away.name ?? 'Away'}</option>
          </select>
        )}
        {viewer.persona === 'player' && (
          <select value={viewer.player ?? 'H10'} aria-label="Player" onChange={(e) => onChange({ ...viewer, player: e.target.value })}>
            {players.map((p) => <option key={p.id} value={p.id}>{p.name} ({p.team})</option>)}
          </select>
        )}
      </fieldset>

      <Pitch info={state.info} recent={state.recent} highlightPlayer={viewer.persona === 'player' ? viewer.player : undefined}>
        <Overlays info={state.info} overlays={state.overlays[key] ?? {}} single />
      </Pitch>
      <CommentaryBar lines={state.commentary[viewer.lang]} />

      {viewer.persona === 'player' && <PlayerStrip snapshot={state.snapshot} playerId={viewer.player} />}

      <ol className="fan-stories">
        {stories.length === 0 && <li className="muted">Stories for this viewer appear here.</li>}
        {stories.slice(0, 4).map((c) => {
          const moment = state.moments[c.momentId]
          return (
            <li key={c.id}>
              <div className="card-log-head">
                <span className="feed-min">{c.minute}'</span>
                {c.source === 'agent' ? <span className="chip chip-green">Verified</span> : <span className="chip">Template</span>}
                {moment && <button type="button" className="why-link" onClick={() => onWhy(moment, c)}>Why did that happen?</button>}
              </div>
              <strong>{c.headline}</strong>
              {c.body && <p>{c.body}</p>}
            </li>
          )
        })}
      </ol>
    </article>
  )
}

function PlayerStrip({ snapshot, playerId }: { snapshot?: MatchSnapshot; playerId?: string }) {
  const p = snapshot?.players.find((x) => x.id === playerId)
  if (!p) return null
  const tiles: [string, string][] = [
    ['Passes', `${p.passesCompleted}/${p.passes}`],
    ['Accuracy', p.passAccuracyPct != null ? `${p.passAccuracyPct.toFixed(0)}%` : '–'],
    ['Distance', `${p.distanceKm.toFixed(1)} km`],
    ['Top speed', p.topSpeedKmh ? `${p.topSpeedKmh.toFixed(1)} km/h` : '–'],
    ['xG', p.xg.toFixed(2)],
  ]
  return (
    <div className="player-strip">
      {tiles.map(([label, value]) => <div key={label}><strong>{value}</strong><span>{label}</span></div>)}
    </div>
  )
}

import { useEffect, useState } from 'react'
import { STUDIO, useReplay } from './useReplay'
import { Pitch } from './components/Pitch'
import { Overlays } from './components/Overlays'
import { Scoreboard } from './components/Scoreboard'
import { StatsPanel } from './components/StatsPanel'
import { EventTicker } from './components/Feeds'
import { ControlRoom } from './components/ControlRoom'
import { CrewBar } from './components/CrewBar'
import { FanView } from './components/FanView'
import { WhyReplay } from './components/WhyReplay'
import { DEFAULT_VIEWERS, spec, type Viewer } from './viewers'
import type { InsightCard, MatchInfo, Moment } from './types'

const SPEEDS = [1, 5, 10, 20, 60]
type View = 'studio' | 'fans'

export default function App() {
  const { state, start, stop } = useReplay()
  const [seed, setSeed] = useState(7)
  const [speed, setSpeed] = useState(20)
  const [crewOn, setCrewOn] = useState(true)
  const [view, setView] = useState<View>('fans')
  const [viewers, setViewers] = useState<Viewer[]>(DEFAULT_VIEWERS)
  const [preview, setPreview] = useState<MatchInfo>()
  const [why, setWhy] = useState<{ moment: Moment; story?: InsightCard }>()
  const running = state.status === 'live' || state.status === 'connecting'

  // Load the selected match's squads before kick-off, so the viewer pickers can name players.
  useEffect(() => {
    const ctrl = new AbortController()
    fetch(`/api/matches/${seed}`, { signal: ctrl.signal })
      .then((r) => (r.ok ? r.json() : undefined))
      .then(setPreview)
      .catch(() => {})
    return () => ctrl.abort()
  }, [seed])

  const openWhy = (momentId: string) => {
    const moment = state.moments[momentId]
    if (moment) setWhy({ moment, story: state.stories[STUDIO]?.find((c) => c.momentId === momentId) })
  }

  const controlRoom = <ControlRoom messages={state.crew} moments={state.moments} tally={state.tally} onWhy={openWhy} />

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark" aria-hidden>●</span> Virtual Studio Crew
        </div>
        <form className="controls" onSubmit={(e) => { e.preventDefault(); start(seed, speed, crewOn, crewOn ? viewers.map(spec) : []) }}>
          <label>Match seed
            <input type="number" min={1} value={seed} onChange={(e) => setSeed(Number(e.target.value) || 1)} />
          </label>
          <label>Speed
            <select value={speed} onChange={(e) => setSpeed(Number(e.target.value))}>
              {SPEEDS.map((s) => <option key={s} value={s}>{s}×</option>)}
            </select>
          </label>
          <label className="toggle">
            <input type="checkbox" checked={crewOn} onChange={(e) => setCrewOn(e.target.checked)} /> AI crew
          </label>
          <button type="submit" className="primary">{running ? 'Restart' : 'Kick off'}</button>
          {running && <button type="button" onClick={stop}>Stop</button>}
        </form>
        <span className={`status status-${state.status}`}>{state.status.replace('_', ' ')}</span>
      </header>

      <Scoreboard info={state.info} snapshot={state.snapshot} status={state.status} />
      <CrewBar active={state.speaker?.id} state={state.speaker?.state} />

      <nav className="tabs" aria-label="View">
        <button type="button" className={view === 'fans' ? 'tab-active' : ''} onClick={() => setView('fans')}>Fan view</button>
        <button type="button" className={view === 'studio' ? 'tab-active' : ''} onClick={() => setView('studio')}>Studio</button>
      </nav>

      {view === 'fans' ? (
        <>
          <FanView state={state} viewers={viewers} preview={preview} running={running} onChange={setViewers}
            onWhy={(moment, story) => setWhy({ moment, story })} />
          {controlRoom}
        </>
      ) : (
        <main className="layout">
          <div className="stage">
            <Pitch info={state.info} recent={state.recent}>
              <Overlays info={state.info} overlays={state.overlays[STUDIO] ?? {}} />
            </Pitch>
            <div className="below-stage">
              {controlRoom}
              <EventTicker info={state.info} feed={state.feed} />
            </div>
          </div>
          <StatsPanel info={state.info} snapshot={state.snapshot} />
        </main>
      )}

      {why && <WhyReplay info={state.info} moment={why.moment} events={state.events} story={why.story} onClose={() => setWhy(undefined)} />}

      <footer className="footer">All clubs, players and match data are synthetic. Characters by Timilehin Owolabi.</footer>
    </div>
  )
}

import { useState } from 'react'
import { useReplay } from './useReplay'
import { Pitch } from './components/Pitch'
import { Overlays } from './components/Overlays'
import { Scoreboard } from './components/Scoreboard'
import { StatsPanel } from './components/StatsPanel'
import { CardLog, EventTicker } from './components/Feeds'
import { CrewBar } from './components/CrewBar'

const SPEEDS = [1, 5, 10, 20, 60]

export default function App() {
  const { state, start, stop } = useReplay()
  const [seed, setSeed] = useState(7)
  const [speed, setSpeed] = useState(20)
  const running = state.status === 'live' || state.status === 'connecting'

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark" aria-hidden>●</span> Virtual Studio Crew
        </div>
        <form className="controls" onSubmit={(e) => { e.preventDefault(); start(seed, speed) }}>
          <label>Match seed
            <input type="number" min={1} value={seed} onChange={(e) => setSeed(Number(e.target.value) || 1)} />
          </label>
          <label>Speed
            <select value={speed} onChange={(e) => setSpeed(Number(e.target.value))}>
              {SPEEDS.map((s) => <option key={s} value={s}>{s}×</option>)}
            </select>
          </label>
          <button type="submit" className="primary">{running ? 'Restart' : 'Kick off'}</button>
          {running && <button type="button" onClick={stop}>Stop</button>}
        </form>
        <span className={`status status-${state.status}`}>{state.status.replace('_', ' ')}</span>
      </header>

      <Scoreboard info={state.info} snapshot={state.snapshot} status={state.status} />
      <CrewBar />

      <main className="layout">
        <div className="stage">
          <Pitch info={state.info} recent={state.recent}>
            <Overlays info={state.info} overlays={state.overlays} />
          </Pitch>
          <div className="below-stage">
            <EventTicker info={state.info} feed={state.feed} />
            <CardLog cards={state.cards} />
          </div>
        </div>
        <StatsPanel info={state.info} snapshot={state.snapshot} />
      </main>

      <footer className="footer">All clubs, players and match data are synthetic.</footer>
    </div>
  )
}

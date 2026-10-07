import type { MatchInfo, MatchSnapshot, TeamSnapshot } from '../types'

interface Props { info?: MatchInfo; snapshot?: MatchSnapshot }

const ROWS: { label: string; get: (t: TeamSnapshot) => number | undefined; fmt?: (v: number) => string; lowerIsBetter?: boolean }[] = [
  { label: 'Possession', get: (t) => t.possessionPct, fmt: (v) => `${v.toFixed(0)}%` },
  { label: 'xG', get: (t) => t.xg, fmt: (v) => v.toFixed(2) },
  { label: 'Shots (on target)', get: (t) => t.shots },
  { label: 'Passes', get: (t) => t.passes },
  { label: 'Pass accuracy', get: (t) => t.passAccuracyPct, fmt: (v) => `${v.toFixed(0)}%` },
  { label: 'Avg pass difficulty', get: (t) => t.avgPassDifficulty * 100, fmt: (v) => v.toFixed(0) },
  { label: 'Progressive passes', get: (t) => t.progressivePasses },
  { label: 'PPDA', get: (t) => t.ppda, fmt: (v) => v.toFixed(1), lowerIsBetter: true },
]

export function StatsPanel({ info, snapshot }: Props) {
  if (!info || !snapshot) return <section className="panel stats-panel"><h2>Match stats</h2><p className="muted">Waiting for kick-off…</p></section>
  const { live } = snapshot
  const momentumPct = 50 + live.momentum / 2

  return (
    <section className="panel stats-panel">
      <h2>Live indicators <span className="muted">last 5 min</span></h2>

      <div className="indicator">
        <div className="indicator-label"><span>Momentum</span><span>{live.momentum > 0 ? info.home.shortName : live.momentum < 0 ? info.away.shortName : 'Even'}</span></div>
        <div className="momentum-bar">
          <div className="momentum-home" style={{ width: `${momentumPct}%`, background: info.home.colour }} />
          <div className="momentum-away" style={{ width: `${100 - momentumPct}%`, background: info.away.colour }} />
          <div className="momentum-mid" />
        </div>
      </div>

      <div className="indicator">
        <div className="indicator-label"><span>Control</span><span>Chaos</span></div>
        <div className="cvc">
          <Gauge label={info.home.shortName} value={live.homeControl} colour={info.home.colour} />
          <Gauge label={info.away.shortName} value={live.awayControl} colour={info.away.colour} />
          <Gauge label="Chaos" value={live.chaos} colour="#ffd166" />
        </div>
      </div>

      <div className="mini-stats">
        <div><strong>{live.tempoPassesPerMin.toFixed(1)}</strong><span>passes / min</span></div>
        <div><strong>{live.turnoversPerMin.toFixed(1)}</strong><span>turnovers / min</span></div>
        <div><strong>{live.homePpda10?.toFixed(1) ?? '–'} / {live.awayPpda10?.toFixed(1) ?? '–'}</strong><span>PPDA 10 min</span></div>
      </div>

      <h2>Match stats</h2>
      <table className="stat-table">
        <thead>
          <tr><th style={{ color: info.home.colour }}>{info.home.shortName}</th><th /><th style={{ color: info.away.colour }}>{info.away.shortName}</th></tr>
        </thead>
        <tbody>
          {ROWS.map((r) => {
            const h = r.get(snapshot.home)
            const a = r.get(snapshot.away)
            const fmt = r.fmt ?? ((v: number) => String(v))
            const extra = r.label.startsWith('Shots') ? [` (${snapshot.home.shotsOnTarget})`, ` (${snapshot.away.shotsOnTarget})`] : ['', '']
            const homeLeads = h != null && a != null && (r.lowerIsBetter ? h < a : h > a)
            const awayLeads = h != null && a != null && (r.lowerIsBetter ? a < h : a > h)
            return (
              <tr key={r.label}>
                <td className={homeLeads ? 'lead' : ''}>{h == null ? '–' : fmt(h) + extra[0]}</td>
                <th scope="row">{r.label}</th>
                <td className={awayLeads ? 'lead' : ''}>{a == null ? '–' : fmt(a) + extra[1]}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </section>
  )
}

function Gauge({ label, value, colour }: { label: string; value: number; colour: string }) {
  return (
    <div className="gauge">
      <div className="gauge-track"><div className="gauge-fill" style={{ height: `${Math.min(value, 100)}%`, background: colour }} /></div>
      <strong>{value.toFixed(0)}</strong>
      <span>{label}</span>
    </div>
  )
}

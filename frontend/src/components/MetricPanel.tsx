import type { MatchInfo } from '../types'
import type { MetricPoint } from '../useReplay'
import { METRICS, type Metric } from '../viewers'

interface Props { info?: MatchInfo; metric: Metric; history: MetricPoint[] }

// PPDA spikes when a team makes almost no defensive actions; past 30 it just means "not pressing".
const PRESSING_CAP = 30

const FORMAT: Record<Metric, (v: number) => string> = {
  xg: (v) => v.toFixed(2),
  passing: (v) => v.toFixed(0),
  pressing: (v) => (v >= PRESSING_CAP ? `${PRESSING_CAP}+` : v.toFixed(1)),
  speed: (v) => (v > 0 ? `${v.toFixed(1)}` : '–'),
}

/** One metric, all match: both teams' live value and how it has moved since kick-off. */
export function MetricPanel({ info, metric, history }: Props) {
  const meta = METRICS.find((m) => m.id === metric)!
  const series = (side: 'home' | 'away') => history
    .map((p) => ({ x: p.clock, y: p[side][metric] }))
    .filter((p): p is { x: number; y: number } => p.y != null)
    .map((p) => (metric === 'pressing' ? { ...p, y: Math.min(p.y, PRESSING_CAP) } : p))
  const home = series('home')
  const away = series('away')
  const all = [...home, ...away]
  const maxY = Math.max(1e-6, ...all.map((p) => p.y))
  const W = 100, H = 30
  const path = (pts: { x: number; y: number }[]) =>
    pts.map((p, i) => `${i ? 'L' : 'M'}${((p.x / 5700) * W).toFixed(1)},${(H - (p.y / maxY) * (H - 2) - 1).toFixed(1)}`).join(' ')
  const now = (pts: { y: number }[]) => (pts.length ? FORMAT[metric](pts[pts.length - 1].y) : '–')

  return (
    <div className="metric-panel">
      <div className="metric-head">
        <strong>{meta.label}</strong>
        <span className="muted">{meta.explain}</span>
      </div>
      <div className="metric-values">
        <div style={{ color: info?.home.colour }}><b>{now(home)}</b><span>{info?.home.shortName ?? 'Home'}</span></div>
        <div style={{ color: info?.away.colour }}><b>{now(away)}</b><span>{info?.away.shortName ?? 'Away'}</span></div>
      </div>
      <svg viewBox={`0 0 ${W} ${H}`} className="metric-chart" preserveAspectRatio="none" role="img" aria-label={`${meta.label} over the match`}>
        <line x1={W / 2} y1={0} x2={W / 2} y2={H} className="metric-half" />
        {home.length > 1 && <path d={path(home)} stroke={info?.home.colour} />}
        {away.length > 1 && <path d={path(away)} stroke={info?.away.colour} />}
      </svg>
    </div>
  )
}

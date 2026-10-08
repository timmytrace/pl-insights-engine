import type { MatchInfo, MatchSnapshot } from '../types'
import type { Phase, Status } from '../useReplay'
import { clockLabel } from '../format'

interface Props { info?: MatchInfo; snapshot?: MatchSnapshot; status: Status; phase: Phase }

const PHASE_LABEL: Record<Phase, string> = { pre: 'Pre-match', '1H': '1st half', HT: 'Half-time', '2H': '2nd half', FT: 'Full-time' }

export function Scoreboard({ info, snapshot, status, phase }: Props) {
  if (!info) return <div className="scoreboard scoreboard-empty">Pick a match and press Kick off</div>
  const clock = snapshot ? clockLabel(snapshot.clock, snapshot.period) : '00:00'
  // Progress through the 90 minutes; stoppage time shows on the clock as 45+2' or 90+3'.
  const progress = phase === 'FT' ? 100 : Math.min(100, ((snapshot?.clock ?? 0) / 5400) * 100)
  return (
    <div className="scoreboard">
      <div className="sb-team" style={{ '--team': info.home.colour } as React.CSSProperties}>
        <span className="sb-swatch" />
        <span className="sb-name">{info.home.shortName}</span>
      </div>
      <div className="sb-score">
        {snapshot?.homeScore ?? 0}<span>–</span>{snapshot?.awayScore ?? 0}
      </div>
      <div className="sb-team sb-away" style={{ '--team': info.away.colour } as React.CSSProperties}>
        <span className="sb-name">{info.away.shortName}</span>
        <span className="sb-swatch" />
      </div>
      <div className="sb-clock">
        <span>{status === 'full_time' || phase === 'FT' ? 'FT' : phase === 'HT' ? 'HT' : clock}</span>
        <small>{PHASE_LABEL[phase]}</small>
      </div>
      <div className="sb-progress" role="progressbar" aria-label="Match progress" aria-valuenow={Math.round(progress)} aria-valuemin={0} aria-valuemax={100}>
        <div style={{ width: `${progress}%` }} />
        <span className="sb-half-mark" />
      </div>
      <div className="sb-meta">{info.home.name} v {info.away.name} · {info.venue} · {info.weather}</div>
    </div>
  )
}

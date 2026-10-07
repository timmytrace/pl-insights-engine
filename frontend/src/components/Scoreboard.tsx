import type { MatchInfo, MatchSnapshot } from '../types'
import type { Status } from '../useReplay'
import { clockLabel } from '../format'

interface Props { info?: MatchInfo; snapshot?: MatchSnapshot; status: Status }

export function Scoreboard({ info, snapshot, status }: Props) {
  if (!info) return <div className="scoreboard scoreboard-empty">Pick a match and press Kick off</div>
  const clock = snapshot ? clockLabel(snapshot.clock, snapshot.period) : '00:00'
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
      <div className="sb-clock">{status === 'full_time' ? 'FT' : clock}</div>
      <div className="sb-meta">{info.home.name} v {info.away.name} · {info.venue} · {info.weather}</div>
    </div>
  )
}

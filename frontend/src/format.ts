import type { MatchEvent, MatchInfo, Side } from './types'

export function clockLabel(clock: number, period: number): string {
  const base = period === 1 ? 45 : 90
  const minute = Math.floor(clock / 60)
  if (minute >= base) {
    const extra = minute - base + 1
    return `${base}+${extra}'`
  }
  const sec = Math.floor(clock % 60).toString().padStart(2, '0')
  return `${minute.toString().padStart(2, '0')}:${sec}`
}

export function playerName(info: MatchInfo | undefined, id: string | undefined): string {
  if (!info || !id) return ''
  for (const team of [info.home, info.away]) {
    const p = [...team.starters, ...team.bench].find((x) => x.id === id)
    if (p) return p.name
  }
  return id
}

export function teamColour(info: MatchInfo | undefined, side: Side): string {
  return info ? info[side].colour : side === 'home' ? '#d7263d' : '#3f88c5'
}

/** A one-line caption for an event, built only from the event's own data. */
export function describeEvent(info: MatchInfo | undefined, e: MatchEvent): string {
  const who = playerName(info, e.playerId)
  const team = info ? info[e.team].name : e.team
  const km = (v?: number) => (v == null ? '' : `${v.toFixed(0)} km/h`)
  const m = (v?: number) => (v == null ? '' : `${v.toFixed(0)} m`)
  const pressed = e.underPressure ? ' under pressure' : ''
  switch (e.type) {
    case 'kickoff': return `${who} kicks off`
    case 'pass':
      if (e.outcome === 'complete') return `${who} finds ${playerName(info, e.receiverId)}, ${m(e.distanceM)}${pressed}`
      if (e.outcome === 'out') return `${who}'s pass runs out of play`
      return `${who}'s pass is cut out${pressed}`
    case 'carry': return e.outcome === 'complete' ? `${who} carries it ${m(e.distanceM)}` : `${who} is dispossessed`
    case 'pressure': return `${who} closes down the ball`
    case 'tackle': return `${who} wins it with a tackle`
    case 'interception': return `${who} intercepts`
    case 'foul': return `Foul by ${who}`
    case 'shot': return `${who} shoots: ${e.outcome?.replace('_', ' ')}, xG ${e.xg?.toFixed(2)}, ${km(e.ballSpeedKmh)}`
    case 'sprint': return `${who} sprints at ${e.playerSpeedKmh?.toFixed(1)} km/h`
    case 'possession_change': return `${team} take over (${e.outcome?.replace('_', ' ')})`
    case 'substitution': return `${who} comes on`
    default: return e.type
  }
}

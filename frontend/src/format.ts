import type { MatchInfo, Side } from './types'

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

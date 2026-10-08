// Mirrors the backend wire format (camelCase properties, snake_case enum values).

export type Side = 'home' | 'away'

export type EventType =
  | 'kickoff' | 'pass' | 'carry' | 'shot' | 'tackle' | 'interception' | 'pressure'
  | 'foul' | 'possession_change' | 'sprint' | 'substitution' | 'period_end'

export interface Player { id: string; name: string; number: number; position: string }

export interface Team {
  side: Side
  name: string
  shortName: string
  colour: string
  formation: string
  starters: Player[]
  bench: Player[]
}

export interface MatchInfo {
  matchId: string
  seed: number
  competition: string
  venue: string
  weather: string
  home: Team
  away: Team
}

export interface MatchEvent {
  id: string
  seq: number
  t: number
  clock: number
  period: number
  type: EventType
  team: Side
  possession: number
  playerId?: string
  x?: number
  y?: number
  endX?: number
  endY?: number
  outcome?: string
  receiverId?: string
  underPressure: boolean
  ballSpeedKmh?: number
  playerSpeedKmh?: number
  distanceM?: number
  bodyPart?: string
  xg?: number
  subOffId?: string
  /** Tracking frame: all 22 players, on-ball events only. */
  players?: PlayerPosition[]
}

export interface PlayerPosition { id: string; x: number; y: number }

export interface EventMetrics {
  event: MatchEvent
  passDifficulty?: number
  progressive: boolean
  keyPassCredit: boolean
  keyPasserId?: string
}

export interface TeamSnapshot {
  possessionPct: number
  passes: number
  passesCompleted: number
  passAccuracyPct: number
  avgPassDistanceM: number
  avgPassDifficulty: number
  progressivePasses: number
  shots: number
  shotsOnTarget: number
  goals: number
  xg: number
  tackles: number
  interceptions: number
  pressures: number
  fouls: number
  ppda?: number
}

export interface LiveSnapshot {
  momentum: number
  homeControl: number
  awayControl: number
  chaos: number
  homePpda10?: number
  awayPpda10?: number
  tempoPassesPerMin: number
  turnoversPerMin: number
}

export interface PlayerSnapshot {
  id: string
  name: string
  number: number
  position: string
  side: Side
  onPitch: boolean
  minutes: number
  touches: number
  passes: number
  passesCompleted: number
  passAccuracyPct?: number
  avgPassDifficulty?: number
  keyPasses: number
  shots: number
  goals: number
  xg: number
  tackles: number
  interceptions: number
  pressures: number
  sprints: number
  topSpeedKmh: number
  distanceKm: number
  maxShotSpeedKmh: number
}

export interface MatchSnapshot {
  matchId: string
  period: number
  clock: number
  minute: number
  homeScore: number
  awayScore: number
  home: TeamSnapshot
  away: TeamSnapshot
  live: LiveSnapshot
  players: PlayerSnapshot[]
}

export type MomentKind =
  | 'goal' | 'big_chance' | 'shot_speed' | 'sprint_speed' | 'elite_pass' | 'milestone'
  | 'press_surge' | 'momentum_swing' | 'chaos_spell' | 'control_spell'

export interface Moment {
  id: string
  kind: MomentKind
  t: number
  clock: number
  minute: number
  team: Side
  playerId?: string
  playerName?: string
  severity: number
  evidence: string[]
  facts: Record<string, string | number | boolean>
}

export type Slot = 'lower_third' | 'player_tag' | 'corner' | 'banner'

export interface CardStat { label: string; value: string; unit?: string }

export interface InsightCard {
  id: string
  momentId: string
  matchId: string
  kind: MomentKind
  team: Side
  playerId?: string
  showAtT: number
  clock: number
  minute: number
  durationS: number
  priority: number
  slot: Slot
  headline: string
  body: string
  stats: CardStat[]
  evidence: string[]
  persona: string
  language: string
  source: 'template' | 'agent'
  replaces?: string
  viewer?: string
}

export type CrewRoleId = 'stats' | 'gaffer' | 'ref' | 'gallery' | 'host'

export type CrewMessageKind = 'brief' | 'tool_call' | 'pitch' | 'verdict' | 'decision' | 'on_air'

export interface CrewMessage {
  momentId: string
  from: CrewRoleId
  kind: CrewMessageKind
  text: string
  matchT: number
  data?: {
    approved?: boolean
    claimsChecked?: number
    numbersChecked?: number
    reasons?: string[]
    air?: boolean
    route?: 'crew' | 'templateonly'
    round?: number
    [key: string]: unknown
  }
}

export type Envelope =
  | { type: 'info'; t: number; data: MatchInfo }
  | { type: 'event'; t: number; data: EventMetrics }
  | { type: 'snapshot'; t: number; data: MatchSnapshot }
  | { type: 'moment'; t: number; data: Moment }
  | { type: 'card'; t: number; data: InsightCard }
  | { type: 'crew'; t: number; data: CrewMessage }
  | { type: 'end'; t: number; data: MatchSnapshot }

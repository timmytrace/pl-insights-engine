// Viewer profiles for the Fan View. The spec string is what the backend parses:
// analyst.en · casual.es · club.fr.home · player.en.H10 · metric.en.pressing

export type Persona = 'analyst' | 'casual' | 'club' | 'player' | 'metric'
export type Metric = 'xg' | 'passing' | 'pressing' | 'speed'
export type Lang = 'en' | 'es' | 'fr'

export interface Viewer {
  persona: Persona
  lang: Lang
  club?: 'home' | 'away'
  player?: string
  metric?: Metric
}

export const PERSONAS: { id: Persona; label: string; blurb: string }[] = [
  { id: 'analyst', label: 'Analyst', blurb: 'Every story, every number' },
  { id: 'casual', label: 'Casual fan', blurb: 'The big moments, plain words' },
  { id: 'club', label: 'Club fan', blurb: 'Their club first, "we" and "us"' },
  { id: 'player', label: 'Player focus', blurb: 'One player, all match' },
  { id: 'metric', label: 'Metric focus', blurb: 'One number they care about, all match' },
]

export const METRICS: { id: Metric; label: string; explain: string }[] = [
  { id: 'xg', label: 'Expected goals (xG)', explain: 'How many goals the chances were worth' },
  { id: 'passing', label: 'Pass difficulty', explain: 'How ambitious the passing is, out of 100' },
  { id: 'pressing', label: 'Pressing (PPDA)', explain: 'Passes allowed per defensive action, last 10 min. Lower = harder press' },
  { id: 'speed', label: 'Top speed', explain: 'Fastest sprint by a player on each team, km/h' },
]

export const LANGS: { id: Lang; label: string }[] = [
  { id: 'en', label: 'English' },
  { id: 'es', label: 'Español' },
  { id: 'fr', label: 'Français' },
]

export const DEFAULT_VIEWERS: Viewer[] = [
  { persona: 'analyst', lang: 'en' },
  { persona: 'casual', lang: 'es' },
  { persona: 'player', lang: 'fr', player: 'H10' },
  { persona: 'metric', lang: 'en', metric: 'pressing' },
]

export function spec(v: Viewer): string {
  if (v.persona === 'club') return `club.${v.lang}.${v.club ?? 'home'}`
  if (v.persona === 'player') return `player.${v.lang}.${v.player ?? 'H10'}`
  if (v.persona === 'metric') return `metric.${v.lang}.${v.metric ?? 'xg'}`
  return `${v.persona}.${v.lang}`
}

export const personaLabel = (p: Persona) => PERSONAS.find((x) => x.id === p)!.label

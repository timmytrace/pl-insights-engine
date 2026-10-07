// Viewer profiles for the Fan View. The spec string is what the backend parses:
// analyst.en · casual.es · club.fr.home · player.en.H10

export type Persona = 'analyst' | 'casual' | 'club' | 'player'
export type Lang = 'en' | 'es' | 'fr'

export interface Viewer {
  persona: Persona
  lang: Lang
  club?: 'home' | 'away'
  player?: string
}

export const PERSONAS: { id: Persona; label: string; blurb: string }[] = [
  { id: 'analyst', label: 'Analyst', blurb: 'Every story, every number' },
  { id: 'casual', label: 'Casual fan', blurb: 'The big moments, plain words' },
  { id: 'club', label: 'Club fan', blurb: 'Their club first, "we" and "us"' },
  { id: 'player', label: 'Player focus', blurb: 'One player, all match' },
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
]

export function spec(v: Viewer): string {
  if (v.persona === 'club') return `club.${v.lang}.${v.club ?? 'home'}`
  if (v.persona === 'player') return `player.${v.lang}.${v.player ?? 'H10'}`
  return `${v.persona}.${v.lang}`
}

export const personaLabel = (p: Persona) => PERSONAS.find((x) => x.id === p)!.label

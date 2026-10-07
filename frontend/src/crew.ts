// The Virtual Studio Crew. Characters and renders by Timilehin Owolabi.
// Colours match each character's plinth ring in the 3D kit.

export type CrewId = 'stats' | 'gaffer' | 'ref' | 'gallery' | 'host'

export interface CrewMember {
  id: CrewId
  name: string
  role: string
  job: string
  colour: string
  avatar: string
  audience: 'control_room' | 'fans'
}

export const CREW: CrewMember[] = [
  { id: 'stats', name: 'Stats', role: 'Data Analyst', job: 'Pulls the numbers. Never gives an opinion.', colour: '#19c2a0', avatar: '/crew/avatars/stats.png', audience: 'control_room' },
  { id: 'gaffer', name: 'The Gaffer', role: 'Tactician', job: 'Pitches the story behind the numbers.', colour: '#3b6cff', avatar: '/crew/avatars/gaffer.png', audience: 'control_room' },
  { id: 'ref', name: 'Ref', role: 'Fact-Checker', job: 'Rejects any claim the evidence does not support.', colour: '#d4e335', avatar: '/crew/avatars/ref.png', audience: 'control_room' },
  { id: 'gallery', name: 'Gallery', role: 'Producer', job: 'Decides what goes on air, when, and what gets dropped.', colour: '#9b5cff', avatar: '/crew/avatars/gallery.png', audience: 'control_room' },
  { id: 'host', name: 'The Host', role: 'Presenter', job: 'Tells each fan the story their way, in their language.', colour: '#ff3b47', avatar: '/crew/avatars/host.png', audience: 'fans' },
]

export const crewMember = (id: CrewId): CrewMember => CREW.find((c) => c.id === id)!

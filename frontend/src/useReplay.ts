import { useCallback, useEffect, useRef, useState } from 'react'
import type { CrewMessage, CrewRoleId, Envelope, EventMetrics, InsightCard, MatchEvent, MatchInfo, MatchSnapshot, Moment, Slot } from './types'

export type Status = 'idle' | 'connecting' | 'live' | 'full_time' | 'error'

export interface ActiveCard { card: InsightCard; until: number }

export interface CrewTally {
  claimsChecked: number
  numbersChecked: number
  sentBack: number
  approved: number
  upgraded: number
  dropped: number
}

export interface ReplayState {
  status: Status
  info?: MatchInfo
  snapshot?: MatchSnapshot
  recent: EventMetrics[]        // newest last, for the pitch trail
  feed: EventMetrics[]          // newest first, notable events for the ticker
  moments: Record<string, Moment>
  crew: CrewMessage[]           // newest last, the Control Room transcript
  tally: CrewTally
  speaker?: { id: CrewRoleId; state: string; until: number }
  /** On-screen graphics per audience: 'studio' or a viewer spec. */
  overlays: Record<string, Partial<Record<Slot, ActiveCard>>>
  /** Recent stories per audience, newest first, upgraded in place when the crew's version lands. */
  stories: Record<string, InsightCard[]>
  /** Every event so far, for the "Why did that happen?" replay. */
  events: Record<string, MatchEvent>
}

export const STUDIO = 'studio'

const TRAIL = 8
const FEED = 40
const CREW_LOG = 240
const FEED_TYPES = new Set(['shot', 'tackle', 'interception', 'foul', 'substitution', 'kickoff', 'period_end'])
const MIN_ON_SCREEN_MS = 3500
const SPEAKER_MS = 1800
const STORIES = 12
const TAG_KINDS = new Set(['shot_speed', 'sprint_speed', 'milestone'])

const emptyTally: CrewTally = { claimsChecked: 0, numbersChecked: 0, sentBack: 0, approved: 0, upgraded: 0, dropped: 0 }
const initial: ReplayState = { status: 'idle', recent: [], feed: [], moments: {}, crew: [], tally: emptyTally, overlays: {}, stories: {}, events: {} }

/**
 * Connects to the replay socket and folds the stream into render state. Overlay slots hold
 * one card each: a higher-priority card (lower number) replaces the current one. A verified
 * agent card replaces its template in place if the template is still on screen.
 */
export function useReplay() {
  const [state, setState] = useState<ReplayState>(initial)
  const socket = useRef<WebSocket | null>(null)
  const speedRef = useRef(20)

  const stop = useCallback(() => {
    socket.current?.close()
    socket.current = null
  }, [])

  const start = useCallback((seed: number, speed: number, crew: boolean, viewers: string[]) => {
    stop()
    speedRef.current = speed
    setState({ ...initial, status: 'connecting' })
    const proto = location.protocol === 'https:' ? 'wss' : 'ws'
    const query = new URLSearchParams({ seed: String(seed), speed: String(speed) })
    if (!crew) query.set('crew', 'off')
    if (viewers.length) query.set('viewers', viewers.join(','))
    const ws = new WebSocket(`${proto}://${location.host}/ws/replay?${query}`)
    socket.current = ws

    ws.onmessage = (msg) => {
      const env = JSON.parse(msg.data) as Envelope
      setState((s) => reduce(s, env, speedRef.current))
    }
    ws.onerror = () => setState((s) => ({ ...s, status: 'error' }))
    ws.onclose = () => setState((s) => (s.status === 'live' || s.status === 'connecting' ? { ...s, status: s.snapshot ? 'full_time' : 'error' } : s))
  }, [stop])

  // Expire overlays and the speaking highlight on a timer so they clear when the stream is quiet.
  useEffect(() => {
    const id = setInterval(() => {
      const now = Date.now()
      setState((s) => {
        let changed = false
        const overlays: ReplayState['overlays'] = {}
        for (const [audience, slots] of Object.entries(s.overlays)) {
          const kept = { ...slots }
          for (const slot of Object.keys(kept) as Slot[]) {
            if (kept[slot]!.until <= now) { delete kept[slot]; changed = true }
          }
          overlays[audience] = kept
        }
        const speaker = s.speaker && s.speaker.until <= now ? undefined : s.speaker
        if (speaker !== s.speaker) changed = true
        return changed ? { ...s, overlays, speaker } : s
      })
    }, 250)
    return () => clearInterval(id)
  }, [])

  useEffect(() => stop, [stop])

  return { state, start, stop }
}

function reduce(s: ReplayState, env: Envelope, speed: number): ReplayState {
  switch (env.type) {
    case 'info':
      return { ...s, status: 'live', info: env.data }
    case 'event': {
      const recent = [...s.recent, env.data].slice(-TRAIL)
      const feed = FEED_TYPES.has(env.data.event.type) ? [env.data, ...s.feed].slice(0, FEED) : s.feed
      return { ...s, recent, feed, events: { ...s.events, [env.data.event.id]: env.data.event } }
    }
    case 'snapshot':
      return { ...s, snapshot: env.data }
    case 'moment':
      return { ...s, moments: { ...s.moments, [env.data.id]: env.data } }
    case 'card':
      return showCard(s, env.data, speed)
    case 'crew':
      return {
        ...s,
        crew: [...s.crew, env.data].slice(-CREW_LOG),
        tally: tally(s.tally, env.data),
        speaker: {
          id: env.data.from,
          state: env.data.kind === 'verdict' ? (env.data.data?.approved === false ? 'rejected' : 'approved') : env.data.kind,
          until: Date.now() + SPEAKER_MS,
        },
      }
    case 'end':
      return { ...s, status: 'full_time', snapshot: env.data }
  }
}

function showCard(s: ReplayState, card: InsightCard, speed: number): ReplayState {
  const audience = card.viewer ?? STUDIO
  const stories = recordStory(s.stories, audience, card)
  const now = Date.now()
  const slots = s.overlays[audience] ?? {}
  const current = slots[card.slot]
  // Match-time durations are compressed by the replay speed, with a floor so cards stay readable.
  const until = now + Math.max((card.durationS * 1000) / speed, MIN_ON_SCREEN_MS)
  const upgradesOnScreen = card.replaces != null && current?.card.id === card.replaces
  const free = !current || current.until <= now || card.priority <= current.card.priority
  if (!upgradesOnScreen && !free) return { ...s, stories }
  return { ...s, stories, overlays: { ...s.overlays, [audience]: { ...slots, [card.slot]: { card, until } } } }
}

/** Keep a short per-audience list of story cards; a crew upgrade replaces its template in place. */
function recordStory(all: ReplayState['stories'], audience: string, card: InsightCard): ReplayState['stories'] {
  if (TAG_KINDS.has(card.kind)) return all
  const list = all[audience] ?? []
  const at = card.replaces ? list.findIndex((c) => c.id === card.replaces) : -1
  const next = at >= 0 ? list.map((c, i) => (i === at ? card : c)) : [card, ...list].slice(0, STORIES)
  return { ...all, [audience]: next }
}

function tally(t: CrewTally, m: CrewMessage): CrewTally {
  if (m.kind === 'verdict') {
    return {
      ...t,
      claimsChecked: t.claimsChecked + (m.data?.claimsChecked ?? 0),
      numbersChecked: t.numbersChecked + (m.data?.numbersChecked ?? 0),
      sentBack: t.sentBack + (m.data?.approved ? 0 : 1),
      approved: t.approved + (m.data?.approved ? 1 : 0),
    }
  }
  if (m.kind === 'on_air') return { ...t, upgraded: t.upgraded + 1 }
  if (m.kind === 'decision' && m.data?.air === false) return { ...t, dropped: t.dropped + 1 }
  return t
}

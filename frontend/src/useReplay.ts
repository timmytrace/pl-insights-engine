import { useCallback, useEffect, useRef, useState } from 'react'
import type { CommentaryLine, CrewMessage, CrewRoleId, Envelope, EventMetrics, InsightCard, MatchEvent, MatchInfo, MatchSnapshot, Moment, Slot } from './types'
import { CommentaryVoice } from './commentaryVoice'

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
  /** Live commentary per language, newest last. */
  commentary: Record<string, CommentaryLine[]>
  /** A message from the server, e.g. when the live crew is busy. */
  notice?: string
  /** Where we are in the match. */
  phase: Phase
  /** Each team's key metrics over the match, sampled every 30 match seconds, for the metric-focus pane. */
  history: MetricPoint[]
}

export interface MetricPoint {
  clock: number
  home: { xg: number; passing: number; pressing?: number; speed: number }
  away: { xg: number; passing: number; pressing?: number; speed: number }
}

export type Phase = 'pre' | '1H' | 'HT' | '2H' | 'FT'
export type Pace = 'condensed' | number

export interface VoiceSettings { on: boolean; lang: string }

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
const COMMENTARY = 4
const initial: ReplayState = { status: 'idle', recent: [], feed: [], moments: {}, crew: [], tally: emptyTally, overlays: {}, stories: {}, events: {}, commentary: {}, phase: 'pre', history: [] }

/**
 * Connects to the replay socket and folds the stream into render state. Overlay slots hold
 * one card each: a higher-priority card (lower number) replaces the current one. A verified
 * agent card replaces its template in place if the template is still on screen.
 */
export function useReplay(voice: VoiceSettings) {
  const [state, setState] = useState<ReplayState>(initial)
  const socket = useRef<WebSocket | null>(null)
  const speedRef = useRef(20)
  const voiceRef = useRef(voice)
  const speaker = useRef<CommentaryVoice | null>(null)

  useEffect(() => {
    voiceRef.current = voice
    if (!voice.on) speaker.current?.stop()
  }, [voice])

  const stop = useCallback(() => {
    socket.current?.close()
    socket.current = null
    speaker.current?.stop()
  }, [])

  const start = useCallback((seed: number, pace: Pace, crew: boolean, viewers: string[]) => {
    stop()
    // Overlay timings scale with the replay speed; a condensed match averages about 10x.
    speedRef.current = pace === 'condensed' ? 10 : pace
    setState({ ...initial, status: 'connecting' })
    const proto = location.protocol === 'https:' ? 'wss' : 'ws'
    const query = new URLSearchParams({ seed: String(seed) })
    if (pace === 'condensed') query.set('mode', 'condensed')
    else query.set('speed', String(pace))
    if (!crew) query.set('crew', 'off')
    if (viewers.length) query.set('viewers', viewers.join(','))
    const ws = new WebSocket(`${proto}://${location.host}/ws/replay?${query}`)
    socket.current = ws
    speaker.current = new CommentaryVoice(seed)

    ws.onmessage = (msg) => {
      const env = JSON.parse(msg.data) as Envelope
      if (env.type === 'commentary' && env.data.voiced && voiceRef.current.on && env.data.language === voiceRef.current.lang)
        speaker.current?.say(env.data)
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
    case 'notice':
      return { ...s, notice: env.data.text }
    case 'commentary': {
      const lang = env.data.language
      return { ...s, commentary: { ...s.commentary, [lang]: [...(s.commentary[lang] ?? []), env.data].slice(-COMMENTARY) } }
    }
    case 'event': {
      const e = env.data.event
      const recent = [...s.recent, env.data].slice(-TRAIL)
      const feed = FEED_TYPES.has(e.type) ? [env.data, ...s.feed].slice(0, FEED) : s.feed
      const phase: Phase = e.type === 'period_end' ? (e.period === 1 ? 'HT' : 'FT')
        : e.type === 'kickoff' && e.clock === 0 ? '1H'
        : e.type === 'kickoff' && e.clock === 2700 ? '2H' : s.phase
      return { ...s, recent, feed, phase, events: { ...s.events, [e.id]: e } }
    }
    case 'snapshot': {
      const snap = env.data
      const last = s.history.at(-1)
      if (last && snap.clock - last.clock < 30) return { ...s, snapshot: snap }
      const top = (side: 'home' | 'away') => Math.max(0, ...snap.players.filter((p) => p.side === side).map((p) => p.topSpeedKmh))
      const point: MetricPoint = {
        clock: snap.clock,
        home: { xg: snap.home.xg, passing: snap.home.avgPassDifficulty * 100, pressing: snap.live.homePpda10, speed: top('home') },
        away: { xg: snap.away.xg, passing: snap.away.avgPassDifficulty * 100, pressing: snap.live.awayPpda10, speed: top('away') },
      }
      return { ...s, snapshot: snap, history: [...s.history, point] }
    }
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
      return { ...s, status: 'full_time', phase: 'FT', snapshot: env.data }
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

import { useCallback, useEffect, useRef, useState } from 'react'
import type { Envelope, EventMetrics, InsightCard, MatchInfo, MatchSnapshot, Moment, Slot } from './types'

export type Status = 'idle' | 'connecting' | 'live' | 'full_time' | 'error'

export interface ActiveCard { card: InsightCard; until: number }

export interface ReplayState {
  status: Status
  info?: MatchInfo
  snapshot?: MatchSnapshot
  recent: EventMetrics[]        // newest last, for the pitch trail
  feed: EventMetrics[]          // newest first, notable events for the ticker
  moments: Moment[]             // newest first
  cards: InsightCard[]          // newest first, the full log
  overlays: Partial<Record<Slot, ActiveCard>>
}

const TRAIL = 8
const FEED = 40
const FEED_TYPES = new Set(['shot', 'tackle', 'interception', 'foul', 'substitution', 'kickoff', 'period_end'])
const MIN_ON_SCREEN_MS = 3500

const initial: ReplayState = { status: 'idle', recent: [], feed: [], moments: [], cards: [], overlays: {} }

/**
 * Connects to the replay socket and folds the stream into render state. Overlay slots hold
 * one card each: a higher-priority card (lower number) replaces the current one, otherwise
 * the new card waits until the slot frees up or is dropped if it goes stale.
 */
export function useReplay() {
  const [state, setState] = useState<ReplayState>(initial)
  const socket = useRef<WebSocket | null>(null)
  const speedRef = useRef(20)

  const stop = useCallback(() => {
    socket.current?.close()
    socket.current = null
  }, [])

  const start = useCallback((seed: number, speed: number) => {
    stop()
    speedRef.current = speed
    setState({ ...initial, status: 'connecting' })
    const proto = location.protocol === 'https:' ? 'wss' : 'ws'
    const ws = new WebSocket(`${proto}://${location.host}/ws/replay?seed=${seed}&speed=${speed}`)
    socket.current = ws

    ws.onmessage = (msg) => {
      const env = JSON.parse(msg.data) as Envelope
      setState((s) => reduce(s, env, speedRef.current))
    }
    ws.onerror = () => setState((s) => ({ ...s, status: 'error' }))
    ws.onclose = () => setState((s) => (s.status === 'live' || s.status === 'connecting' ? { ...s, status: s.snapshot ? 'full_time' : 'error' } : s))
  }, [stop])

  // Expire overlays on a timer so they clear even when the stream is quiet.
  useEffect(() => {
    const id = setInterval(() => {
      const now = Date.now()
      setState((s) => {
        let changed = false
        const overlays = { ...s.overlays }
        for (const slot of Object.keys(overlays) as Slot[]) {
          if (overlays[slot]!.until <= now) { delete overlays[slot]; changed = true }
        }
        return changed ? { ...s, overlays } : s
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
      return { ...s, recent, feed }
    }
    case 'snapshot':
      return { ...s, snapshot: env.data }
    case 'moment':
      return { ...s, moments: [env.data, ...s.moments] }
    case 'card': {
      const card = env.data
      const now = Date.now()
      const current = s.overlays[card.slot]
      const free = !current || current.until <= now || card.priority <= current.card.priority
      // Match-time durations are compressed by the replay speed, with a floor so cards stay readable.
      const until = now + Math.max((card.durationS * 1000) / speed, MIN_ON_SCREEN_MS)
      return {
        ...s,
        cards: [card, ...s.cards],
        overlays: free ? { ...s.overlays, [card.slot]: { card, until } } : s.overlays,
      }
    }
    case 'end':
      return { ...s, status: 'full_time', snapshot: env.data }
  }
}

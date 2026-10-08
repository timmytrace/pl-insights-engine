import type { CommentaryLine } from './types'

/**
 * Plays the big-moment commentary lines. Lines are queued (at most two waiting, so the voice
 * never falls far behind play); each is fetched from the server, which only voices lines it
 * wrote itself. Without Azure Speech the browser's own voice reads the line instead.
 */
export class CommentaryVoice {
  private queue: CommentaryLine[] = []
  private playing = false
  private audio?: HTMLAudioElement
  private readonly seed: number

  constructor(seed: number) {
    this.seed = seed
  }

  say(line: CommentaryLine) {
    // A goal jumps the queue; otherwise drop the oldest if we're falling behind.
    if (line.excitement >= 3) this.queue = [line]
    else this.queue = [...this.queue, line].slice(-2)
    if (!this.playing) void this.next()
  }

  stop() {
    this.queue = []
    this.audio?.pause()
    speechSynthesis.cancel()
    this.playing = false
  }

  private async next() {
    const line = this.queue.shift()
    if (!line) { this.playing = false; return }
    this.playing = true
    const done = () => void this.next()
    try {
      const res = await fetch(`/api/matches/${this.seed}/commentary/${line.seq}/audio?lang=${line.language}`)
      if (!res.ok) throw new Error('no audio')
      const url = URL.createObjectURL(await res.blob())
      this.audio = new Audio(url)
      this.audio.onended = () => { URL.revokeObjectURL(url); done() }
      this.audio.onerror = done
      await this.audio.play()
    } catch {
      const u = new SpeechSynthesisUtterance(line.text)
      u.lang = line.language
      u.rate = line.excitement >= 3 ? 1.2 : line.excitement === 2 ? 1.1 : 1
      u.onend = done
      u.onerror = done
      speechSynthesis.speak(u)
    }
  }
}

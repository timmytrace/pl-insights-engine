import { useEffect, useState } from 'react'

// Clip names the Control Room asks for, e.g. "ref-rejected". Files are listed in
// /crew/clips/manifest.json; anything not listed falls back to the still avatar.
let manifest: Promise<Record<string, string>> | undefined

function loadManifest(): Promise<Record<string, string>> {
  manifest ??= fetch('/crew/clips/manifest.json')
    .then((r) => (r.ok ? r.json() : { clips: {} }))
    .then((m: { clips?: Record<string, string> }) => m.clips ?? {})
    .catch(() => ({}))
  return manifest
}

export function useClips(): Record<string, string> {
  const [clips, setClips] = useState<Record<string, string>>({})
  useEffect(() => { loadManifest().then(setClips) }, [])
  return clips
}

/** Which clip a crew message should play. */
export function clipState(kind: string, approved?: boolean): string {
  if (kind === 'verdict') return approved === false ? 'rejected' : 'approved'
  return kind
}

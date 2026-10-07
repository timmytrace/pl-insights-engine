import { crewMember, type CrewId } from '../crew'
import { useClips } from '../clips'

interface Props {
  id: CrewId
  size: number
  /** The state to animate, e.g. "rejected" or "on_air". Omit for the still portrait. */
  state?: string
}

/** A crew portrait that plays the character's animated clip for `state` when one exists. */
export function Avatar({ id, size, state }: Props) {
  const clips = useClips()
  const member = crewMember(id)
  const clip = state ? clips[`${id}-${state}`] : undefined
  return (
    <img src={clip ? `/crew/clips/${clip}` : member.avatar} alt="" width={size} height={size}
      className={clip ? 'avatar avatar-animated' : 'avatar'} />
  )
}

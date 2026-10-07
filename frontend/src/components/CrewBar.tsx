import { CREW, type CrewId } from '../crew'
import { Avatar } from './Avatar'

/** The crew on duty. `active` highlights whoever is speaking and plays their clip for `state`. */
export function CrewBar({ active, state }: { active?: CrewId; state?: string }) {
  return (
    <section className="crew-bar" aria-label="Studio crew">
      {CREW.map((c) => (
        <figure key={c.id} className={`crew-member${active === c.id ? ' crew-active' : ''}`}
          style={{ '--crew': c.colour } as React.CSSProperties} title={c.job}>
          <Avatar id={c.id} size={56} state={active === c.id ? state : undefined} />
          <figcaption>
            <strong>{c.name}</strong>
            <span>{c.role}</span>
          </figcaption>
        </figure>
      ))}
    </section>
  )
}

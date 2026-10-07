import { CREW, type CrewId } from '../crew'

/** The crew on duty. `active` highlights whoever is working on the current moment. */
export function CrewBar({ active }: { active?: CrewId }) {
  return (
    <section className="crew-bar" aria-label="Studio crew">
      {CREW.map((c) => (
        <figure key={c.id} className={`crew-member${active === c.id ? ' crew-active' : ''}`}
          style={{ '--crew': c.colour } as React.CSSProperties} title={c.job}>
          <img src={c.avatar} alt="" width={56} height={56} />
          <figcaption>
            <strong>{c.name}</strong>
            <span>{c.role}</span>
          </figcaption>
        </figure>
      ))}
    </section>
  )
}

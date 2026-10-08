# Crew evaluation

Generated 2026-10-08 by `dotnet run --project src/Studio.Cli -c Release -- evaluate`, on 30 story moments from matches 3, 7, with the live crew on Azure OpenAI (`gpt-4.1-mini`).

## Quality, scored by an LLM judge (1–5)

Each story's on-screen text was scored with `Microsoft.Extensions.AI.Evaluation.Quality` (groundedness against the moment's fact sheet, relevance, coherence, fluency). "First draft" is The Gaffer's first pitch, before Ref; "Final" is what aired.

| Text | Stories | Groundedness | Relevance | Coherence | Fluency |
|---|---|---|---|---|---|
| The Gaffer's first draft | 30 | 4.93 | 3.60 | 3.97 | 3.73 |
| Final, on air | 30 | 4.93 | 3.43 | 3.87 | 3.77 |
| Sent back by Ref: first draft | 14 | 4.93 | 3.71 | 4.00 | 3.64 |
| Sent back by Ref: final | 14 | 4.93 | 3.36 | 3.79 | 3.71 |
| The Host, casual fan in Spanish | 11 | 4.36 | 3.18 | 3.82 | 3.55 |

## Ref's record

| Measure | Value |
|---|---|
| Approved first time | 53% |
| Sent back at least once | 47% |
| Drafts per story (mean) | 1.47 |
| Stories that aired | 100% |
| Never cleared (template stayed) | 0 |
| Moment to verified card, median | 10.8 s |
| Moment to verified card, 90th percentile | 17.0 s |

### Why Ref sent pitches back

| Reason | Times |
|---|---|
| Cited a fact that doesn't exist | 11 |
| Reviewer agent: claim doesn't follow from the facts | 6 |
| Quoted a match total too early | 3 |
| Season, record or certainty claim | 1 |

### Examples

**5' ElitePass** (match 3)

- First draft: Vance's pressured elite pass sparks attack. At 5 minutes, Jonas Vance delivers a difficult, progressive 34.6m pass under pressure with perfect accuracy, setting up Pavel Okafor. Despite Cobalt Park's negative momentum, their control remains strong at 73.3%.
- Ref: "Jonas Vance completes a difficult 34.6m progressive pass under pressure with 100% accuracy" cites 'Distance', which isn't on the fact sheet.
- On air: Vance's elite pass under pressure drives play. At 5 minutes, Jonas Vance delivers a difficult, progressive pass under pressure with perfect accuracy to Pavel Okafor. Cobalt Park controls 73.3% of the game despite negative momentum.

**7' ElitePass** (match 3)

- First draft: Long progressive pass opens play. Yusuf Marsh delivers a difficult 42m elite pass at 82 km/h without pressure, progressing the ball effectively to Ruben Yilmaz. Ashvale United trails in momentum and control early on but creates quality chances.
- Ref: "Yusuf Marsh's pass was 41.8m long and at 81.8 km/h, showing high difficulty and speed without pressure" cites 'Distance', which isn't on the fact sheet. "Yusuf Marsh's pass was 41.8m long and at 81.8 km/h, showing high difficulty and speed without pressure" cites 'ball speed kmh', which isn't on the fact sheet.
- On air: Marsh's elite pass advances attack. Yusuf Marsh completes a difficult, progressive pass to Ruben Yilmaz early in the match. Ashvale United trails in momentum and control but is creating promising opportunities despite the pressure.

**9' BigChance** (match 3)

- First draft: Okafor's big chance saved early. At 9 minutes, Okafor's close-range shot with 0.34 xG was saved, reflecting Cobalt Park's control and momentum with 64.4% possession and 69.2 momentum in the last 5 minutes.
- Ref: It's minute 9: possession for the match doesn't mean anything yet.
- On air: Okafor's shot saved despite strong control. At 9 minutes, Okafor's 0.34 xG shot from 8.6 meters was saved, but Cobalt Park shows strong control with a 73.6 control index and positive momentum of 69.2 in the last 5 minutes.

## Caveats

- The judge is the same model family as the crew, which can flatter it; the before/after comparison is the meaningful signal, not the absolute scores.
- Groundedness is judged against the fact sheet as plain text, the same facts the crew saw.
- Raw per-story results are in [`results.json`](results.json).

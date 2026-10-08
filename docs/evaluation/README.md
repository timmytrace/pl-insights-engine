# Crew evaluation

Generated 2026-10-08 by `dotnet run --project src/Studio.Cli -c Release -- evaluate`, on 30 story moments from matches 3, 7, with the live crew on Azure OpenAI (`gpt-4.1-mini`).

## How we got here

| Run | What changed | Approved first time | Median time to verified card |
|---|---|---|---|
| [1. Baseline](history/1-baseline/README.md) | First live evaluation | 53% | 10.8 s |
| [2. Citation fix](history/2-citation-fix/README.md) | Ref resolves citations by key or label (same matches as run 1) | 77% | 8.5 s |
| 3. This report | Simulator recalibrated to real goal and shot rates (new matches) | 87% | 9.9 s |

Run 1 showed that Ref's most common rejection, 11 of 21, wasn't a factual error: The Gaffer cited
a fact by its label instead of its key, and the revision dropped the detail, lowering relevance
while catching nothing untrue. Runs 1 and 2 use the same matches, so that change is like for like.
Run 3 uses the recalibrated simulator, whose matches differ, so compare it to the others with care.

What Ref rejects now is what it's there for: match totals quoted too early, claims the reviewer
agent judged didn't follow from the facts, and the occasional season or certainty claim.
Groundedness is already high in first drafts (≈4.9 of 5), so in practice the rulebook works as a
guarantee more than a frequent correction.

## Quality, scored by an LLM judge (1–5)

Each story's on-screen text was scored with `Microsoft.Extensions.AI.Evaluation.Quality` (groundedness against the moment's fact sheet, relevance, coherence, fluency). "First draft" is The Gaffer's first pitch, before Ref; "Final" is what aired.

| Text | Stories | Groundedness | Relevance | Coherence | Fluency |
|---|---|---|---|---|---|
| The Gaffer's first draft | 30 | 4.97 | 3.70 | 3.90 | 3.87 |
| Final, on air | 30 | 4.93 | 3.67 | 3.90 | 3.87 |
| Sent back by Ref: first draft | 4 | 5.00 | 3.75 | 3.75 | 3.75 |
| Sent back by Ref: final | 4 | 4.75 | 3.50 | 3.75 | 3.75 |
| The Host, casual fan in Spanish | 9 | 4.56 | 3.33 | 3.89 | 3.78 |

## Ref's record

| Measure | Value |
|---|---|
| Approved first time | 87% |
| Sent back at least once | 13% |
| Drafts per story (mean) | 1.17 |
| Stories that aired | 100% |
| Never cleared (template stayed) | 0 |
| Moment to verified card, median | 9.9 s |
| Moment to verified card, 90th percentile | 14.6 s |

### Why Ref sent pitches back

| Reason | Times |
|---|---|
| Quoted a match total too early | 3 |
| Reviewer agent: claim doesn't follow from the facts | 3 |
| Season, record or certainty claim | 1 |

### Examples

**5' ElitePass** (match 3)

- First draft: Long pressured pass breaks midfield deadlock. Dario Adeyemi completes a difficult 35.5m progressive pass under pressure to Kwame Ibarra, showing Cobalt Park's intent despite negative momentum and high chaos early on.
- Ref: It's minute 5: possession for the match doesn't mean anything yet.
- On air: Dario Adeyemi’s pressured long pass shines. At minute 5, Adeyemi completes a tough 35.5m progressive pass under pressure to Kwame Ibarra, highlighting Cobalt Park’s early attempts to control play amid high chaos and negative momentum.

**13' Goal** (match 3)

- First draft: Vance heads home early opener. Jonas Vance scores a low-xg headed goal from 23 meters, assisted by Pavel Okafor. Despite Cobalt Park's negative momentum, they control possession and pressing intensity.
- Ref: It's minute 13: possession for the match doesn't mean anything yet.
- On air: Vance heads a rare long-range goal. Jonas Vance scores from a rare headed shot with low xG at 23 meters, assisted by Pavel Okafor. Cobalt Park shows strong control and pressing despite negative momentum.

**5' ElitePass** (match 7)

- First draft: Mensah's elite pass breaks pressure. At 5 minutes, Gabriel Mensah executes a difficult 42m progressive pass under pressure, completing all his passes with 100% accuracy. This shows Saltmarsh City's controlled build despite Harbour Town's higher possession and control.
- Ref: It's minute 5: possession for the match doesn't mean anything yet.
- On air: Mensah's precise long pass eases pressure. At 5 minutes, Gabriel Mensah delivers a difficult 42m progressive pass under pressure with perfect accuracy, helping Saltmarsh City build momentum early in the match.

## Caveats

- The judge is the same model family as the crew, which can flatter it; the before/after comparison is the meaningful signal, not the absolute scores.
- Groundedness is judged against the fact sheet as plain text, the same facts the crew saw.
- Raw per-story results are in [`results.json`](results.json).

# Crew evaluation

Generated 2026-10-08 by `dotnet run --project src/Studio.Cli -c Release -- evaluate`, on 30 story moments from matches 3, 7, with the live crew on Azure OpenAI (`gpt-4.1-mini`).

## What the evaluation changed

The first run ([`before-citation-fix/`](../1-baseline/README.md)) showed that Ref's most
common rejection, 11 of 21, wasn't a factual error at all: The Gaffer cited a fact by its label
("Distance") instead of its key (`distanceM`). The revision then dropped the detail, so Ref was
lowering relevance (3.60 → 3.43) while catching nothing untrue. Ref now resolves citations by key
or label. Same matches, same moments, re-run:

| Measure | Before | After |
|---|---|---|
| Approved first time | 53% | 77% |
| Rejections for citation bookkeeping | 11 | 0 |
| Drafts per story | 1.47 | 1.27 |
| Moment to verified card, median / 90th percentile | 10.8 s / 17.0 s | 8.5 s / 12.9 s |
| Relevance of what aired | 3.43 | 3.53 |

What remains are the rejections Ref is there for: quoting match totals too early, and claims the
reviewer agent judged didn't follow from the facts. Groundedness was already high in first
drafts (≈4.9 of 5), so in practice the rulebook works as a guarantee more than a frequent
correction.

## Quality, scored by an LLM judge (1–5)

Each story's on-screen text was scored with `Microsoft.Extensions.AI.Evaluation.Quality` (groundedness against the moment's fact sheet, relevance, coherence, fluency). "First draft" is The Gaffer's first pitch, before Ref; "Final" is what aired.

| Text | Stories | Groundedness | Relevance | Coherence | Fluency |
|---|---|---|---|---|---|
| The Gaffer's first draft | 30 | 4.93 | 3.53 | 3.97 | 3.83 |
| Final, on air | 30 | 4.93 | 3.53 | 3.97 | 3.83 |
| Sent back by Ref: first draft | 7 | 4.86 | 3.57 | 3.86 | 3.86 |
| Sent back by Ref: final | 7 | 4.86 | 3.57 | 3.86 | 3.86 |
| The Host, casual fan in Spanish | 11 | 4.45 | 3.36 | 3.91 | 3.91 |

## Ref's record

| Measure | Value |
|---|---|
| Approved first time | 77% |
| Sent back at least once | 23% |
| Drafts per story (mean) | 1.27 |
| Stories that aired | 100% |
| Never cleared (template stayed) | 0 |
| Moment to verified card, median | 8.5 s |
| Moment to verified card, 90th percentile | 12.9 s |

### Why Ref sent pitches back

| Reason | Times |
|---|---|
| Quoted a match total too early | 5 |
| Reviewer agent: claim doesn't follow from the facts | 5 |

### Examples

**4' ElitePass** (match 3)

- First draft: Ulloa's elite pass under pressure. Viktor Ulloa completes a 48m progressive pass at 97.5 km/h under pressure, showing precise control early in the match. Cobalt Park holds 68.4% possession but faces momentum challenges.
- Ref: It's minute 4: possession for the match doesn't mean anything yet.
- On air: Ulloa's precise elite pass early on. At minute 4, Viktor Ulloa delivers a 48m progressive pass at 97.5 km/h under pressure with perfect accuracy. Cobalt Park shows strong control but faces early momentum challenges.

**5' ElitePass** (match 3)

- First draft: Okafor receives under pressure elite pass. Jonas Vance delivers a difficult, progressive pass under pressure to Pavel Okafor from 35 meters at 75 km/h. Despite Cobalt Park's possession dominance, momentum is against them early on.
- Ref: It's minute 5: possession for the match doesn't mean anything yet.
- On air: Vance's elite pass breaks lines early. At minute 5, Jonas Vance executes a high-difficulty, progressive pass under pressure to Pavel Okafor over 34.6 meters at 75 km/h. Despite no shots yet, Cobalt Park faces negative momentum.

**7' ElitePass** (match 3)

- First draft: Long progressive pass opens play. Yusuf Marsh delivers a precise 41.8m pass at 81.8 km/h to Ruben Yilmaz without pressure, showing Ashvale United's intent to break opponent control early in the match.
- Ref: The pass was progressive and not under pressure, which supports control in build-up, so the first claim is approved. The distance and ball speed facts support the quality of the pass, so the second claim is approved.
- On air: Long progressive pass shows control. Yusuf Marsh's 41.8m pass at 81.8 km/h to Ruben Yilmaz is progressive and made without pressure, reflecting Ashvale United's ability to build play under control early in the match.

## Caveats

- The judge is the same model family as the crew, which can flatter it; the before/after comparison is the meaningful signal, not the absolute scores.
- Groundedness is judged against the fact sheet as plain text, the same facts the crew saw.
- Raw per-story results are in [`results.json`](results.json).

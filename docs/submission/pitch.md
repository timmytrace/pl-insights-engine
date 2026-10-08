# Project pitch: Virtual Studio Crew

## One line

An AI production crew on Azure that turns live match events into explained, fact-checked,
personalised broadcast graphics and commentary for every fan, in their language.

## Elevator pitch (for the submission form)

Football broadcasts tell every fan the same story, and AI that writes sports copy makes numbers
up. **Virtual Studio Crew** fixes both. Five agents built on **Microsoft Agent Framework** run as
a workflow over every moment of a synthetic match: Stats freezes the facts, The Gaffer explains
why the moment matters, Ref checks every number and claim against the data and sends back
anything that doesn't hold up, Gallery decides what still deserves air, and The Host rewrites the
verified story for each viewer: analyst or casual fan, their club, one player, or one metric they
care about, in English, Spanish or French. Fans also get live commentary, a "Why did that happen?"
replay of the evidence behind any story, and a spoken full-time recap. It runs on
**Azure AI Foundry** (gpt-4.1-mini, keyless with Entra ID), **Azure AI Speech**, and **Azure
Container Apps**, is evaluated with Foundry as the judge, traced in **Application Insights**, and
exports timed, machine-readable overlays for a broadcaster's graphics engine.

## What it does

- **Real-time match intelligence.** Ingests a stream of synthetic events (passes, carries, shots,
  tackles, pressure, possession changes, sprints, substitutions, with tracking frames) and
  computes pass distance, accuracy and a difficulty rating, ball and shot speed, xG, PPDA,
  momentum and a control-vs-chaos index, live.
- **Moments that matter.** Rules detect goals, big chances, pressing surges, momentum swings,
  spells of control or chaos, standout passes, milestones and speed/distance thresholds, each
  with the facts and the event ids behind it.
- **Explained, then verified.** The agent crew writes why each moment matters. Ref's rulebook
  checks every number against the fact sheet at the precision quoted, rejects season, record and
  certainty claims and match totals quoted too early, then a reviewer agent judges whether the
  claims follow. The template graphic airs instantly, so the crew never slows the broadcast.
- **Made for every fan.** Up to four viewers side by side, each with their own persona, language,
  club, player or metric. Personalisation also decides what a viewer doesn't see. Every version
  is re-checked by Ref in its own language.
- **On screen and on air.** Broadcast-style overlays with name tags on the pitch; live
  play-by-play captions in three languages, voiced by Azure AI Speech on the big moments; a
  spoken full-time recap with a voice for each character; a condensed mode that plays a full match
  in under five minutes.
- **Built for the production path.** A versioned overlay timeline (JSON Schema) for graphics
  partners, live upgrades over WebSocket, usage limits that degrade gracefully, and one-command
  deployment.

## The problem it solves

Broadcasters can't produce a different show for every fan, and generic AI commentary can't be
trusted on air. Virtual Studio Crew makes personalisation cheap and keeps it honest: every
number comes from code, every claim is checked before it airs, and every story can show its evidence.

## Microsoft and Azure technologies

| Technology | Used for |
|---|---|
| **Azure AI Foundry** (Azure OpenAI, gpt-4.1-mini) | The crew's agents and the recap writer, signed in with Microsoft Entra ID (no keys) |
| **Microsoft Agent Framework** | `ChatClientAgent` agents with sessions and tools, orchestrated as a **Workflow** with a review loop |
| **Microsoft.Extensions.AI + Evaluation** | Model access, OpenTelemetry, and quality evaluation (groundedness, relevance, coherence, fluency) with Foundry as the judge |
| **Azure AI Speech** | Neural voices for live commentary and the full-time recap, per character and language |
| **Azure Container Apps** | Hosting the app, API and WebSocket in one container, scaling to zero |
| **Azure Container Registry** | Cloud image builds |
| **Application Insights / Log Analytics** | End-to-end traces of every replay, workflow step, agent run and model call |
| **Managed identity + Bicep** | Least-privilege, keyless access and repeatable infrastructure |

## Evidence

- **Evaluation:** on 30 live stories from two matches, Ref approves 87% of pitches first time
  and the median time from moment to verified card is 9.9 s (14.6 s at the 90th percentile).
  The evaluation found Ref rejecting pitches over citation format rather than facts; fixing that
  raised first-time approval from 53% to 77% on the same matches. Full report in `docs/evaluation`.
- **Data:** 100 synthetic matches calibrated to real top-flight figures (2.9 goals per match,
  1.4 xG and 12 shots per team, 81% pass accuracy), with a data card in `data/synthetic`.
- **Quality:** 92 automated tests and CI on every push.

## Links

- Live demo: https://studio-crew.politebay-c6735be6.canadacentral.azurecontainerapps.io
- Code: https://github.com/timmytrace/pl-insights-engine

## Credits

Built by Timilehin Owolabi. Characters designed and modelled by Timilehin Owolabi. All clubs,
players and match data are synthetic.

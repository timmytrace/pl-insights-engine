# Demo video script: Virtual Studio Crew

**Length:** 1:55 (the judges stop at 2:00). **Voiceover:** ~215 words, about 115 words a minute, so nothing is rushed.
**Music:** none, or a track you made yourself. No third-party music, logos or club names.
**Every shot is the real, deployed app** (the rules require footage of the project working).

## Before you record

- Open the live site: https://studio-crew.politebay-c6735be6.canadacentral.azurecontainerapps.io
  It scales to zero, so load it once a minute before recording to wake it up.
- Browser at 1920×1080, zoom 100%, no bookmarks bar or other tabs showing.
- Match seed **7**, pace **Condensed**, AI crew **on**, 🔊 Commentary **on** (English).
  The four default viewers are what we want: Analyst (EN), Casual fan (ES), Player focus on
  Tomas Oduya (FR), Metric focus: pressing (EN).
- Seed 7's story: a goal at 22', momentum swings, a pressing surge, an equaliser just after
  half-time. Run it once all the way through before recording so you know where the moments fall.
- Record the screen with OBS (free) at 1080p/30. Record the voiceover separately so you can re-take
  lines, then lay it over the footage. Keep the app's own audio (the commentary voice and the recap)
  at the moments marked below.
- Upload to YouTube as **public** and paste the link on the project page.

## Script

| Time | On screen | Voiceover |
|---|---|---|
| **0:00–0:08** | The crew lineup render (`docs/crew-lineup.jpg`), title fades in: *Virtual Studio Crew*. | One match. Millions of fans. And every one of them wants a different broadcast. |
| **0:08–0:22** | Press **Kick off**. The four Fan View panes come alive: players moving, captions in English, Spanish and French. | This is Virtual Studio Crew: an AI production crew on Azure that turns live match events into explained, personalised graphics, for every fan at once. |
| **0:22–0:30** | Zoom on the Control Room as the first story starts: Stats' brief, The Gaffer's pitch. | Behind the screen, five agents work every moment. Stats freezes the facts. The Gaffer pitches why it matters. |
| **0:30–0:42** | Hold on a **Ref "Sent back"** line, e.g. *"It's minute 14: possession for the match doesn't mean anything yet."* Then the revision and **Verified**. (Use Ref's red-card clip here if you've rendered it.) | And Ref checks every number and every claim against the data. Anything that doesn't hold up goes back. Nothing unverified reaches air. |
| **0:42–0:52** | A pane's template graphic swaps for the crew's version with **✓ Verified by Ref**. Let the app's commentary voice play over a shot or the 22' goal. | Gallery, the producer, decides what still deserves air, and The Host presents it, while the commentary calls the match live. |
| **0:52–1:05** | Pan across the four panes on the same moment: analyst numbers, casual Spanish, the player-focus stat strip, the pressing chart. | Same moment, four fans: numbers for the analyst, the story in Spanish for a casual fan, one player for another, pressing for a fourth. Each version re-checked by Ref. |
| **1:05–1:18** | Click **Why did that happen?** on the goal. Let the replay step through the build-up to the verified story. | Any story can explain itself. Why did that happen? The crew replays the exact events behind it, then the verified explanation. |
| **1:18–1:32** | Quick cuts: the workflow diagram in the README; the evaluation table (first-time approval, time to air); an Application Insights trace of one moment. | A Microsoft Agent Framework workflow, evaluated with Foundry as the judge and traced in Application Insights. Evaluation caught Ref being too strict: fixing it lifted first-time approval from 53 to 77 percent. |
| **1:32–1:45** | Full time. The recap panel opens; let **3–4 seconds** of the Azure voices play, with the line highlighting. | At the final whistle, the crew talks you through the match, in your language, every number checked. |
| **1:45–1:55** | End card: *Azure AI Foundry · Microsoft Agent Framework · Azure AI Speech · Azure Container Apps*, the GitHub URL, the live URL. | Virtual Studio Crew. Explained, verified, and made for every fan. |

## Shot checklist

- [ ] Fan View, four panes, captions in three languages
- [ ] Control Room: a "Sent back" by Ref, then "Verified"
- [ ] A template graphic upgrading to "✓ Verified by Ref"
- [ ] Commentary voice audible over a shot or goal
- [ ] "Why did that happen?" replay
- [ ] Player-focus strip and the pressing metric chart
- [ ] Workflow diagram, evaluation table, an Application Insights trace
- [ ] 3–4 seconds of the spoken recap
- [ ] End card with technologies and both URLs

## If something goes wrong while recording

- **A "busy" notice appears:** another viewer has the live crew; wait a minute and restart.
- **No Ref rejection in the run:** they happen in about a quarter of stories. Restart the match, or
  use a moment from the Studio tab's Control Room after full time (it keeps the transcript).
- **Audio is silent:** the first voiced line needs a click on the page first (browser autoplay rules);
  pressing Kick off counts.

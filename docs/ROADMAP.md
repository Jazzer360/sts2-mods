# Roadmap

This is a condensed copy of the "StS2 Mod Suite — Design Notes" doc (https://claude.ai/artifact/RridSZiNPVRXimjbxYM3Rb). The doc has the full design for each idea.

## How the ideas are grouped

The original plan had six one-job mods. Here they're split by what kind of mod they are:

- **Spire QoL** (`mods/SpireQoL`) shows information the game already has, in a clearer place. It never tells you what to pick. Each feature is a toggle.
- **Advisory mods** (later, separate) judge your choices. They need their own data pipeline and are bigger projects.
- **Existing mods we use instead of building**: Relic Stats for StS2, Display the Spire.

| Idea | Goes in | Size | Notes |
| --- | --- | --- | --- |
| Keyword Badges | Spire QoL | S | Retain/Ethereal glyph on cards, read from live card state |
| Relic Reminders+ | Spire QoL | M | Relic icon on affected cards in hand; rules stored as data |
| Intent Forecast | Spire QoL | M | Upcoming enemy moves above the intent, showing how certain each one is |
| Combat Math | Spire QoL | S | Incoming damage vs. block, net damage, per-enemy breakdown |
| Synergy Helper | own mod (later) | M | Synergy/anti-synergy markers on reward/shop cards |
| Deck Metrics | own mod (later) | L | Monte Carlo hand sim, consistency Δ vs. skip |

## Build order

1. ~~Decompile `sts2.dll`~~ done (v0.111.0, in `~/git/sts2-decompiled`). Still to confirm: where card keywords are stored, how monster AI is structured, and which class is the card-reward screen.
2. ~~Repo + core~~ the scaffold is done. Next: a settings layer (per-feature toggles), a hotkey to hide overlays, and a smoke-test script.
3. **Keyword Badges.** It's small, but it uses every piece of the pipeline: a Harmony patch, reading live card state, and adding a UI node to cards.
4. **Relic Reminders+**, starting with Art of War, Ripple Basin and Unceasing Top.
5. **Intent Forecast** and **Combat Math**, in whichever order you miss more.
6. Then the advisory mods: Synergy Helper first, then Deck Metrics.

## Shared-infra principles

- Read live game objects instead of hardcoding values.
- Each feature is patched under its own Harmony id. If a patch fails, only that feature turns off, and the failure goes to the log (`FeatureHost`).
- Display-only mods set `"affects_gameplay": false`, so multiplayer doesn't require everyone to have them.
- Keep extracted data in a language-neutral format (JSON) so it can be shared with `sts2-engine`.

## Open questions

- [ ] Which Mintyspire features do you actually miss? That decides how big Combat Math is.
- [ ] Intent Forecast default: always-on mini row, or show on hover?
- [ ] Try STS2 Card Advisor before building Synergy Helper.
- [ ] Ask jorbs about reading Spirebird data.
- [ ] Workshop release: needs polish, localization, and a settings UI.

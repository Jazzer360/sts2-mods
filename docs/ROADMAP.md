# Roadmap

The full design lives in the "StS2 Mod Suite — Design Notes" doc (https://claude.ai/artifact/RridSZiNPVRXimjbxYM3Rb). Other docs:
- [FEATURES.md](FEATURES.md): what each feature does, kept complete.
- [TRACKER.md](TRACKER.md): the item-by-item work list (relics, card modifiers, damage and block sources).
- [CHANGELOG.md](../CHANGELOG.md): patch notes.

## How the ideas are grouped

- **Spire QoL** (`mods/SpireQoL`) shows information the game already has, in a clearer place. It never tells you what to pick. Each feature is a toggle.
- **Advisory mods** (later, separate) judge your choices: Synergy Helper and Deck Metrics.
- **Existing mods we use instead of building:** Relic Stats for StS2, Display the Spire.

| Spire QoL feature | Size | Covers |
| --- | --- | --- |
| Settings and layout (core) | M | toggles, draggable/anchored indicators, lock/unlock, reset |
| Card Indicators | M | Ethereal translucency, keyword badges, cost/replay duration markers, affliction states |
| Relic Counters | M | condensed tick-bar tray above hand; Pen Nib and Stone Calendar get attention effects |
| Relic Reminders | M | relic icons on affected cards; Pen Nib-ready attack glow |
| Block Forecast | S | end-of-turn block (Plating, Frost, Orichalcum...) beside current block |
| Incoming Damage | S | summed blockable damage incl. Burn/Decay/Constrict; health-bar preview |
| Shuffle Warning | S | draws that force a reshuffle; which hand cards stay out |
| Intent Forecast | M | upcoming enemy moves |

## Build order

1. ~~Decompile and repo scaffold.~~ Done.
2. **Core settings and layout.** Every indicator depends on it, so build it first.
3. **Relic Counters**, starting with Pen Nib, Stone Calendar, Kunai/Shuriken. Highest value; it's generic over `RelicModel.ShowCounter/DisplayAmount/Status`, so most relics come almost free.
4. **Block Forecast** and **Incoming Damage**. They share the "what happens at end of turn" model.
5. **Card Indicators**: Ethereal translucency first, then badges, then markers.
6. **Relic Reminders**, starting with the Pen Nib-ready glow, Art of War, Ripple Basin.
7. **Shuffle Warning.**
8. **Intent Forecast.**
9. The advisory mods.

## Shared-infra principles

- Read live game objects instead of hardcoding values. Thresholds come from each relic's `DynamicVars`.
- Each feature is patched under its own Harmony id. If a patch fails, only that feature turns off, and the failure goes to the log (`FeatureHost`).
- Display-only mods set `"affects_gameplay": false`.
- Never reuse the game's glow or cost colors. Never cover existing card UI (TRACKER §8).
- Keep extracted data language-neutral (JSON) so it can be shared with `sts2-engine`.

## Open questions

- [ ] Which Mintyspire features do you actually miss?
- [ ] Intent Forecast default: always-on mini row, or hover?
- [ ] Does the game have a mod-settings hook, or do we add our own menu button?
- [ ] Try STS2 Card Advisor before building Synergy Helper.
- [ ] Workshop release: polish, localization, settings UI.

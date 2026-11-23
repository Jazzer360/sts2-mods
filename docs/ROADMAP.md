# Roadmap

The full design lives in the "StS2 Mod Suite — Design Notes" doc (https://claude.ai/artifact/RridSZiNPVRXimjbxYM3Rb). Other docs:
- [FEATURES.md](FEATURES.md): what each feature does, kept complete.
- [TRACKER.md](TRACKER.md): the item-by-item work list (relics, card modifiers, damage and block sources).
- [CHANGELOG.md](../CHANGELOG.md): patch notes.

## Goals

What Spire QoL is for. Everything else serves these, and all of them serve **the contract** (decision #92): what the mod shows for each decision you could make right now is exactly what happens, the exact chance of each outcome when a roll decides it, or a `?` where something isn't modelled. Never a certainty the game doesn't keep. Accuracy comes first; speed serves it.

1. **Every card that does something beyond its listed effects gets a tab**, plus a few hand-picked cases (Retain, Sly, replays, Ethereal fading). Good or bad, from a power or a relic, yours or an enemy's: if playing a card sets anything off, the card says so.
2. **Every relic that affects a fight is tracked**: by a tab on the cards it reacts to, a tracker row for its count or timer, or (for always-on relics with nothing to decide) an explicit entry saying so.
3. **Reminders for decisions outside combat**: wherever a relic changes what a choice does (resting, event options, card rewards, shops), an indicator on that choice, as Relic Reminders did in StS1 (Regal Pillow on resting, Omamori on events that add a curse). Bing Bong copying cards added to your deck is the first example.
4. **Only knowable information** (CLAUDE.md, hard rule): what a player with perfect memory and full knowledge of the rules could know, never the RNG or hidden state.
5. **Everything that happens is accounted for.** Every effect in a fight is either predicted (a tab, a tracker row, the simulator) or reported when it happens (audits, coverage check), so gaps surface from players' games instead of hiding. Every prediction is paired with its outcome by an audit; a skipped check, or a branch the simulator doesn't model, is a counted gap to close, not a pass (decision #92). Defaults are tuned afterwards: what shows on cards by default is a settings question, not a coverage one.
6. **Multiplayer is the whole team, evaluated in order** (decision #75). Plays resolve one after another, and every player's powers, relics, orbs and Osty are evaluated together, deterministically: what your card sets off for a teammate (block into their Juggernaut) counts toward its predictions like what it does for you.

**What the simulator is for** (the owner's requirements): it checks the mod's understanding of the game's rules against what the game does, and it tells the mod what a card play leads to, so tabs show exactly when they should. How it's built is secondary to two things: it's written around the game's rules (the code a card runs, the hooks a relic overrides), not the words on each card, and it's ready for game updates (every per-card list has a scan that flags a new card doing the same thing).

## How the ideas are grouped

- **Spire QoL** (`mods/SpireQoL`) shows information the game already has, in a clearer place. It never tells you what to pick. Each feature is a toggle.
- **Main Saves** (`mods/MainSaves`): your normal profiles while modded (decision #33).
- **Advisory mods** (later, separate) judge your choices: Synergy Helper and Deck Metrics.
- **Existing mods we use instead of building:** Relic Stats for StS2, Display the Spire.

| Spire QoL feature | Status | Covers |
| --- | --- | --- |
| Settings and layout | built | the settings menu (Guide and About pages), movable and anchored widgets, lock and reset |
| Proc Tags | built | a tag on each card for what playing it sets off: procs, lethal, kills, replays, returns, free cards (from the outcome simulator) |
| Keyword Tags, Ethereal Fade | built | Retain, Sly, replay and double-play tags; Ethereal cards see-through (decision #185) |
| Relic Counters | built | the tracker beside your HP bar: counters, countdowns, Make It So |
| Incoming Damage | built | the end-of-turn sum, lethal and revives, teammates' on hover |
| Death Skull | built | an enemy that dies before acting if you end your turn now, with its chance |
| Intent Details | built (1.20.0) | the block and powers of the move on screen that its intent doesn't show (decisions #98, #99) |
| Prediction Progress | built (1.18.0) | a clock and bar under End Turn while a prediction is still searching (decision #89) |
| Shuffle Tag | built | plays whose draws reshuffle your discard pile |
| Potion Tags | built | relics that drinking a potion sets off |
| Reward Tags, Event Reminders, Shop Reminders, Map Reminders | built | tags on offered cards; relic icons on events (with card odds and the Inspect screen), shops and the map (Run Reminders until decision #185) |
| Rest Preview | built | HP after resting, Mend, the boss fight's start |
| Update Notice | built | versions behind, on the main menu |
| Intent Forecast | built (1.12.0) | an enemy's next moves where its rules decide them, the odds when a roll does (decision #72, tracker §7) |

## Build order

1. ~~Decompile and repo scaffold, core settings and layout, Relic Counters, Incoming Damage, Card Indicators, proc tabs, Shuffle Warning~~ (0.2.0 to 0.23).
2. ~~The outcome simulator~~ (0.24), rebuilt on the game's own hooks and pipelines in 1.0.0 (decisions #48 to #52).
3. ~~Out-of-combat reminders~~ (Run Reminders, 0.36.0 onward; Rest Preview; pathing).
4. **Multiplayer as one team** (decision #75): cards that act on every player or on a teammate, and everything they set off for the team.
5. ~~Intent Forecast~~ (decision #72, 1.12.0).
6. ~~Development tooling~~: the event stream, session metrics, dev mode and the dev dashboard (decision #108, 1.21.x).
7. The advisory mods.

## Shared-infra principles

- Read live game objects instead of hardcoding values. Thresholds come from each relic's `DynamicVars`.
- Each feature is patched under its own Harmony id. If a patch fails, only that feature turns off, and the failure goes to the log (`FeatureHost`).
- Display-only mods set `"affects_gameplay": false`.
- Never reuse the game's glow or cost colors. Never cover existing card UI (TRACKER §8).
- Keep extracted data language-neutral (JSON) so it can be shared with `sts2-engine`.

## Open questions

- [ ] Which Mintyspire features do you actually miss?
- [x] ~~Intent Forecast: where it shows and how far ahead.~~ On hover, until a roll (decision #72).
- [ ] Try STS2 Card Advisor before building Synergy Helper.
- [ ] Workshop release: polish, localization (decision #8: only through CI).
- [x] ~~Does the game have a mod-settings hook, or do we add our own menu button?~~ Our own: the settings gear on the main menu (1.5.0) and the in-fight hotkey.
- [ ] A relic's chance on its tab? (owner's idea, 2026-11-20) A plain tab shows only when the play sets the relic off in every outcome, and a crossed one when any outcome does, so a proc that only happens in some outcomes shows nothing. Example: Battle Trance with Hellraiser and 3 Strikes in a 10-card draw pile: each Strike it draws plays itself, so Shuriken, Kunai or Ornamental Fan fire, or Pen Nib's count moves (and its doubled hit can land on the auto-played Strike), in some outcomes. Powers too: Iteration on a card that draws, when your draw pile holds a Status (the owner's note, 1.24.2). The simulator already has each outcome's share (as for the lethal tab's chance, decision #88, and the draw chance tab, decision #119), so the tab could carry the chance it fires. Open: whether it's worth it, how it looks next to a proc's amount, and which relics it would apply to. A new kind of mark if built (its own decision, Guide entry and setting).

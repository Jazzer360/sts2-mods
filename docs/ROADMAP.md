# Roadmap

The full design lives in the "StS2 Mod Suite — Design Notes" doc (https://claude.ai/artifact/RridSZiNPVRXimjbxYM3Rb). Other docs:
- [FEATURES.md](FEATURES.md): what each feature does, kept complete.
- [TRACKER.md](TRACKER.md): the item-by-item work list (relics, card modifiers, damage and block sources).
- [CHANGELOG.md](../CHANGELOG.md): patch notes.

## Goals

What Spire QoL is for, in order. Everything else serves these.

1. **Every card that does something beyond its listed effects gets a tab**, plus a few hand-picked cases (Retain, Sly, replays, Ethereal fading). Good or bad, from a power or a relic, yours or an enemy's: if playing a card sets anything off, the card says so.
2. **Every relic that affects a fight is tracked**: by a tab on the cards it reacts to, a tracker row for its count or timer, or (for always-on relics with nothing to decide) an explicit entry saying so.
3. **Reminders for decisions outside combat**: wherever a relic changes what a choice does (resting, event options, card rewards, shops), an indicator on that choice, as Relic Reminders did in StS1 (Regal Pillow on resting, Omamori on events that add a curse). Bing Bong copying cards added to your deck is the first example.
4. **Only knowable information** (CLAUDE.md, hard rule): what a player with perfect memory and full knowledge of the rules could know, never the RNG or hidden state.
5. **Everything that happens is accounted for.** Every effect in a fight is either predicted (a tab, a tracker row, the simulator) or reported when it happens (audits, coverage check), so gaps surface from players' games instead of hiding. Defaults are tuned afterwards: what shows on cards by default is a settings question, not a coverage one.
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
| Proc Indicators | built | a tab on each card for what playing it sets off: procs, lethal, kills, replays, returns, free cards (from the outcome simulator) |
| Card Indicators | built | Retain and Sly tabs, Ethereal translucency |
| Relic Counters | built | the tracker beside your HP bar: counters, countdowns, Make It So |
| Incoming Damage | built | the end-of-turn sum, lethal and revives, teammates' on hover |
| Death Skull | built | an enemy that dies before acting if you end your turn now, with its chance |
| Shuffle Warning | built | plays whose draws reshuffle your discard pile |
| Potion Tabs | built | relics that drinking a potion sets off |
| Run Reminders | built | relic icons on events, card rewards, shops and the map; tabs on offered cards; card odds and the Inspect screen |
| Rest Preview | built | HP after resting, Mend, the boss fight's start |
| Update Notice | built | versions behind, on the main menu |
| Intent Forecast | planned | an enemy's next moves where its rules decide them, the odds when a roll does (decision #72, tracker §7) |

## Build order

1. ~~Decompile and repo scaffold, core settings and layout, Relic Counters, Incoming Damage, Card Indicators, proc tabs, Shuffle Warning~~ (0.2.0 to 0.23).
2. ~~The outcome simulator~~ (0.24), rebuilt on the game's own hooks and pipelines in 1.0.0 (decisions #48 to #52).
3. ~~Out-of-combat reminders~~ (Run Reminders, 0.36.0 onward; Rest Preview; pathing).
4. **Multiplayer as one team** (decision #75): cards that act on every player or on a teammate, and everything they set off for the team.
5. **Intent Forecast** (decision #72).
6. The advisory mods.

## Shared-infra principles

- Read live game objects instead of hardcoding values. Thresholds come from each relic's `DynamicVars`.
- Each feature is patched under its own Harmony id. If a patch fails, only that feature turns off, and the failure goes to the log (`FeatureHost`).
- Display-only mods set `"affects_gameplay": false`.
- Never reuse the game's glow or cost colors. Never cover existing card UI (TRACKER §8).
- Keep extracted data language-neutral (JSON) so it can be shared with `sts2-engine`.

## Open questions

- [ ] Which Mintyspire features do you actually miss?
- [ ] Intent Forecast: where it shows (beside each enemy's intent, always or on hover) and how far ahead.
- [ ] Try STS2 Card Advisor before building Synergy Helper.
- [ ] Workshop release: polish, localization (decision #8: only through CI).
- [x] ~~Does the game have a mod-settings hook, or do we add our own menu button?~~ Our own: the settings gear on the main menu (1.5.0) and the in-fight hotkey.

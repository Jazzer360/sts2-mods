# Spire QoL

Display-only quality-of-life tweaks for Slay the Spire 2. The mod doesn't change gameplay (`affects_gameplay: false`). Every feature can be turned on or off, and every on-screen indicator can be dragged and anchored wherever you want.

| Feature | What you'll see |
| --- | --- |
| Proc Indicators | A tab on each card in your hand for every relic or power (yours or a teammate's) that playing it would set off right now (plain), or that would work against you (crossed); lethal, kill and replay tabs |
| Card Indicators | Retain and Sly tabs, Ethereal cards translucent |
| Relic Counters | A tracker beside your HP bar for counter relics, countdown powers and cards that come back (Pen Nib, Kunai, Stone Calendar, Make It So...) |
| Incoming Damage | A sum by your HP bar: incoming damage minus your end-of-turn block, the HP you'll lose, and a skull when it's lethal; a teammate's on hover |
| Death Skull | A skull by an enemy's HP when it dies before acting if you end your turn now, with the chance when a roll decides it |
| Shuffle Warning | A discard-pile tab on cards whose draws would reshuffle your discard pile |
| Potion Tabs | A tab under each potion for the relics that drinking it sets off |
| Run Reminders | Outside fights: relic icons beside event options, card rewards, shop prices and the map legend; tabs on cards you're offered; the Inspect screen for an option's possible cards |
| Rest Preview | At a rest site, the HP you'll have after resting and the heal wasted |
| Update Notice | On the main menu, how many versions behind you are, and the settings gear |
| Intent Forecast | Hover an enemy's intent: its next moves, stacked above it, where its rules decide them, and each move's chance when a roll does |

The settings menu's Guide page shows how to read every tab and counter. See [docs/FEATURES.md](../../docs/FEATURES.md) for the full description of each feature, [docs/TRACKER.md](../../docs/TRACKER.md) for progress, and [CHANGELOG.md](../../CHANGELOG.md) for patch notes.

## Philosophy

- **Display only.** Spire QoL never changes what happens in the game; it shows what's there more clearly.
- **Only what you could know.** Everything shown is something a player with perfect memory and a complete understanding of the rules could work out from the screen. It never reads the seed or a random roll, or the draw pile's order. Wherever a roll decides something (a random target, an enemy's random next move), you see the range, the odds or the worst case, never the result.
- **Plain or crossed.** A plain tab on a card means playing it does something extra right now. A red slash means it might be bad for you: a warning, not a verdict.
- **Yours to arrange.** Every feature, tab and tracker row can be switched off, and every panel can be moved.
- **Checked against the game.** Predictions are compared with what the game actually did; with error reports on, mismatches are sent so they can be fixed.

## How it's built

The settings menu's About page shows the Philosophy section above and the patch notes from CHANGELOG.md, built into the mod.

Each feature lives in `Features/` and is registered in `ModEntry.cs`. If a game update breaks one feature, that feature turns itself off and logs why. The rest keep working.

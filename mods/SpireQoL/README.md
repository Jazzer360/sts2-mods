# Spire QoL

Display-only quality-of-life tweaks for Slay the Spire 2. The mod doesn't change gameplay (`affects_gameplay: false`). Every feature can be turned on or off, and every on-screen indicator can be dragged and anchored wherever you want.

| Feature | What you'll see |
| --- | --- |
| Proc Tags | A tag on each card in your hand for every relic or power (yours or a teammate's) that playing it would set off right now (plain), or that would work against you (crossed); lethal, kill and replay tags |
| Keyword Tags | Retain, Sly, replay and double-play tags |
| Ethereal Fade | Ethereal cards in your hand see-through |
| Relic Counters | A tracker beside your HP bar for counter relics, countdown powers and cards that come back (Pen Nib, Kunai, Stone Calendar, Make It So...) |
| Incoming Damage | A sum by your HP bar: incoming damage minus your end-of-turn block, the HP you'll lose, and a skull when it's lethal; a teammate's on hover |
| Death Skull | A skull by an enemy's HP when it dies before acting if you end your turn now, with the chance when a roll decides it |
| Shuffle Tag | A discard-pile tag on cards whose draws would reshuffle your discard pile |
| Potion Tags | A tag under each potion for the relics that drinking it sets off |
| Reward Tags | Outside fights: tags on the cards you're offered (card rewards, an event's choice of cards, the shop) for your relics that care about them |
| Event Reminders | Relic icons beside event options, the chance a card relic's cards come up, and the Inspect screen for an option's possible cards |
| Shop Reminders | Relic icons beside shop prices, and the price after Lucky Fysh's gold |
| Map Reminders | Relic icons beside the map legend's rows |
| Rest Preview | At a rest site, the HP you'll have after resting and the heal wasted |
| Update Notice | On the main menu, how many versions behind you are, and the settings gear |
| Intent Forecast | Hover an enemy's intent: its next moves, stacked above it, where its rules decide them, and each move's chance when a roll does |
| Intent Details | On each enemy's intent: the block its move gains and the powers it applies |
| Prediction Progress | A small bar under End Turn, rarely: predictions still working through every outcome |

The settings menu's Guide page shows how to read every tag and counter. See [docs/FEATURES.md](../../docs/FEATURES.md) for the full description of each feature, [docs/TRACKER.md](../../docs/TRACKER.md) for progress, and [CHANGELOG.md](../../CHANGELOG.md) for patch notes.

## Philosophy

- **Display only.** Spire QoL never changes what happens in the game; it shows what's there more clearly.
- **Only what you could know.** Everything shown is something a player with perfect memory and a complete understanding of the rules could work out from the screen. It never reads the seed or a random roll, or the draw pile's order. Wherever a roll decides something (a random target, an enemy's random next move), you see the range, the odds or the worst case, never the result.
- **Plain or crossed.** A plain tag on a card means playing it does something extra right now. A red slash means it might be bad for you: a warning, not a verdict.
- **Yours to arrange.** Every feature, tag and tracker row can be switched off, and every panel can be moved.
- **What's shown is what happens.** A number or tag is exactly what follows from that play or from ending your turn, or the exact chance when a roll decides it; where the mod doesn't model something, it shows `?` rather than a guess.
- **Checked against the game.** Predictions are compared with what the game actually did; with error reports on, mismatches are sent so they can be fixed.

## How it's built

The settings menu's About page shows the Philosophy section above and the patch notes from CHANGELOG.md, built into the mod.

Each feature lives in `Features/` and is registered in `ModEntry.cs`. If a game update breaks one feature, that feature turns itself off and logs why. The rest keep working.

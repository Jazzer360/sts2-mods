# Spire QoL

Display-only quality-of-life tweaks for Slay the Spire 2. The mod doesn't change gameplay (`affects_gameplay: false`). Every feature can be turned on or off, and every on-screen indicator can be dragged and anchored wherever you want.

| Feature | What you'll see |
| --- | --- |
| Card Indicators | On-card tags for Retain, Sly, double play, added Exhaust, Galvanized; blocked cards dimmed with a crossed tag; Ethereal cards translucent |
| Relic Counters | Tick-bar tray beside your HP bar for counter relics and countdown powers (Pen Nib, Kunai, Stone Calendar, Panache...) |
| Relic Reminders | Relic tags on the cards they affect, crossed when playing the card breaks the relic |
| Incoming Damage | Stacked sum by your HP bar: incoming damage − end-of-turn block = damage you will take |
| Shuffle Warning | Discard-pile tag on cards whose draw would reshuffle your discard pile |
| Intent Forecast | Each enemy's upcoming moves (not built yet) |

Everything except Intent Forecast is in the current test build. See [docs/FEATURES.md](../../docs/FEATURES.md) for the full description of each change, [docs/TRACKER.md](../../docs/TRACKER.md) for progress, and [CHANGELOG.md](../../CHANGELOG.md) for patch notes.

Each feature lives in `Features/` and is registered in `ModEntry.cs`. If a game update breaks one feature, that feature turns itself off and logs why. The rest keep working.

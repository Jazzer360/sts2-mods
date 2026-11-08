# Spire QoL

Display-only quality-of-life tweaks for Slay the Spire 2. The mod doesn't change gameplay (`affects_gameplay: false`). Every feature can be turned on or off, and every on-screen indicator can be dragged and anchored wherever you want.

| Feature | What you'll see |
| --- | --- |
| Card Indicators | Ethereal cards turn translucent; badges for Retain/Ethereal/Sly; markers for temporary cost changes and afflictions |
| Relic Counters | Pip tray above your hand for Pen Nib, Kunai, Stone Calendar and every other counter relic |
| Relic Reminders | Relic icons on the cards they affect; attacks glow when Pen Nib is ready |
| Block Forecast | The block you'll gain at end of turn (Plating, Frost orbs...) beside your block |
| Incoming Damage | Total blockable damage this round, including Burn/Decay/Constrict |
| Shuffle Warning | When a draw will reshuffle, and which hand cards stay out |
| Intent Forecast | Each enemy's upcoming moves |

Everything above is still planned. See [docs/FEATURES.md](../../docs/FEATURES.md) for the full description of each change, [docs/TRACKER.md](../../docs/TRACKER.md) for progress, and [CHANGELOG.md](../../CHANGELOG.md) for patch notes.

Each feature lives in `Features/` and is registered in `ModEntry.cs`. If a game update breaks one feature, that feature turns itself off and logs why. The rest keep working.

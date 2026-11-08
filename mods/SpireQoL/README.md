# Spire QoL

Display-only quality-of-life tweaks for Slay the Spire 2. The mod doesn't change gameplay (`affects_gameplay: false`). Every feature can be turned on or off.

| Feature | What you'll see | Status |
| --- | --- | --- |
| Keyword Badges | A Retain or Ethereal glyph on every card that currently has it, including generated cards | Planned |
| Relic Reminders | Relic icons on affected cards in hand; a struck-through icon means playing the card breaks the relic | Planned |
| Intent Forecast | Small faded icons for each enemy's next moves, with odds when the move is random | Planned |
| Combat Math | Incoming damage vs. your block, and the net damage you'll take | Planned |

Each feature lives in `Features/` and is registered in `ModEntry.cs`. If a game update breaks one feature, that feature turns itself off and logs why. The rest keep working.

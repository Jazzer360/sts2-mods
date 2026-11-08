# Spire QoL: feature reference

This is the complete list of every change Spire QoL makes to the game. If a change isn't here, the mod doesn't make it. Each feature lists what you see, its settings, which game UI it touches, and the game methods it patches.

**Status key:** 🟢 shipped · 🟡 in progress · ⚪ planned

Spire QoL is **display-only**. It never changes game state, damage, cards or saves. Its manifest sets `"affects_gameplay": false`, so you can join multiplayer lobbies whose players don't have it. See [MODDING_NOTES.md](MODDING_NOTES.md).

| Feature | Id | Status | Default |
| --- | --- | --- | --- |
| [Settings and layout](#settings-and-layout) | (core) | ⚪ | always on |
| [Card Indicators](#card-indicators) | `card-indicators` | ⚪ | on |
| [Relic Counters](#relic-counters) | `relic-counters` | ⚪ | on |
| [Relic Reminders](#relic-reminders) | `relic-reminders` | ⚪ | on |
| [Block Forecast](#block-forecast) | `block-forecast` | ⚪ | on |
| [Incoming Damage](#incoming-damage) | `incoming-damage` | ⚪ | on |
| [Shuffle Warning](#shuffle-warning) | `shuffle-warning` | ⚪ | on |
| [Intent Forecast](#intent-forecast) | `intent-forecast` | ⚪ | on |

---

## Settings and layout

**What you see:** a Spire QoL entry in settings, and on-screen indicators you can place yourself.

- Turn each feature on or off.
- **Unlock layout** (menu or hotkey): every floating indicator gets an outline and can be dragged. **Lock** freezes them in place and makes them click-through.
- **Anchor:** pin each indicator to a screen corner, edge or center, or to a game element (health bar, hand, draw pile, relic bar). Positions are stored relative to the anchor, so they survive resolution changes.
- Per indicator: scale, opacity, reset to default. **Reset all** restores the whole layout.
- Optional grid snap while dragging. Indicators that end up off-screen are pulled back.
- **Hide all overlays** hotkey.

**Stored in:** a JSON file in the mod's folder. Never written to game saves.

**Game UI touched / patches:** *to be filled in when implemented.*

---

## Card Indicators

**What you see:** mid-combat changes to a card that the game otherwise shows only as text.

- **Ethereal cards are translucent** (default ~70% opacity, adjustable) whenever they are Ethereal this combat, whatever the source: the card itself, Music Box copies, or Hex.
- **Keyword badges** (top-center): Ethereal, Retain (including retain for this turn only), Sly (including this turn only), and Exhaust when it was added. Innate and Eternal badges are available but off by default.
- **Duration markers:** "free this turn", "free this combat", a clock on temporary cost changes, replay count, and "plays twice" next play.
- **Affliction states:** cards that can't be played right now because of Bound, Ringing or Smog are dimmed. Galvanized cards get a "this hurts you" glyph. The game's own affliction overlays are left as they are.

**Settings:** each badge type on/off · Ethereal opacity · badge slot (top-center / corner).

**Game UI touched:** card node (`NCard`). Adds children only; never moves or recolors the cost, title, enchantment tab or affliction overlay. Uses no glow color the game already uses.

**Patches:** *to be filled in when implemented.*

---

## Relic Counters

**What you see:** a tray (by default above your hand) with one row of pips for each counter relic you own. Pips fill as the counter advances.

- The row **pulses** when one step from firing and **blinks** when the next qualifying card will fire it.
- **Pen Nib** blinks hard when ready, and attacks in hand light up (see Relic Reminders).
- **Stone Calendar** shows turns 1–7 and blinks throughout turn 7 ("fires when you end this turn").
- **Inverse counters** show what's left: Velvet Choker (card plays left), Pocketwatch (still within its limit).
- Turn-trigger relics (Horn Cleat, Captain's Wheel, Candelabra, Chandelier, Sparkling Rouge, Happy Flower, Pendulum...) show turns until they fire.

Full relic list: [TRACKER.md §2](TRACKER.md#2-counter-relics--relic-counters-pip-tray-above-the-hand).

**Settings:** per-relic on/off · tray position (movable) · pulse/blink intensity.

**Game UI touched:** none of the game's own nodes. The tray is a new overlay. The relic bar's own counters are left as they are.

**Patches:** *to be filled in when implemented.*

---

## Relic Reminders

**What you see:** the icon of a relic that cares about a card, shown on that card in your hand.

- **Pen Nib ready:** attacks in hand get a pulsing border in a color the game doesn't use. It tells you this attack will consume Pen Nib.
- **Struck-through icon:** playing this card breaks a relic's condition (Art of War, Ripple Basin on attacks).
- **Normal icon:** playing this card triggers or advances a relic (Permafrost, Mummified Hand, Game Piece, Ivory Tile, Chemical X...).
- If several relics apply, one icon with a stack count is shown; hover to expand.

Full relic list: [TRACKER.md §3](TRACKER.md#3-condition-relics--relic-reminders-icons-on-cards-in-hand).

**Settings:** per-relic on/off · icon corner.

**Game UI touched:** card node (`NCard`), top corner only.

**Patches:** *to be filled in when implemented.*

---

## Block Forecast

**What you see:** "+N" next to your block: the block you'll gain automatically at end of turn, so you stop overblocking.

Counts: Plating, Frost orb passives (including extra triggers), Orichalcum, Cloak Clasp, Ripple Basin (if still eligible), Sturdy Clamp carry-over. Horn Cleat and Captain's Wheel are shown the turn before they fire.

Full source list: [TRACKER.md §4](TRACKER.md#4-end-of-turn-block-sources--block-forecast).

**Settings:** position (movable, default beside the player's block) · show breakdown on hover.

**Game UI touched:** player health bar area (`NHealthBar`), alongside the block icon.

**Patches:** *to be filled in when implemented.*

---

## Incoming Damage

**What you see:** the total blockable damage you'll take this round, and how much will get through your current block plus the forecast block.

- Includes enemy attack intents (multi-hit included) and blockable end-of-turn self damage: Burn, Decay, Infection, Toxic, Wither in hand; Constrict, Magic Bomb, Disintegration debuffs.
- **Unblockable** HP loss (Regret, Bad Luck, Beckon, Demise) is shown separately, never mixed into the blockable total.
- The health bar previews the expected loss, next to the game's existing Poison/Doom preview.
- Multiplayer: counts only intents that target you.

Full source list: [TRACKER.md §5](TRACKER.md#5-incoming-damage--incoming-damage).

**Settings:** position (movable) · health-bar preview on/off · per-enemy breakdown on hover.

**Game UI touched:** player health bar (`NHealthBar`).

**Patches:** *to be filled in when implemented.*

---

## Shuffle Warning

**What you see:** a reminder of when a draw will reshuffle your discard pile. Cards in your hand at that moment stay out of the new draw pile, so you can choose what to hold and what to reshuffle.

- "N until shuffle" by the draw pile.
- A shuffle badge on draw cards and potions whose draw would run past the end of the draw pile.
- While a shuffle is possible, cards in hand get a subtle "stays out of the next draw pile" marker.

**Settings:** position of the counter (movable) · hand markers on/off.

**Game UI touched:** draw pile area, card nodes in hand, potion slots.

**Patches:** *to be filled in when implemented.*

---

## Intent Forecast

**What you see:** each enemy's upcoming moves, shrunk and faded above its current intent.

- Moves known for certain: solid icons, 2–3 ahead.
- Random branches: icons side by side with odds, or "?" when the odds can't be worked out.
- Conditional moves: icon with a condition marker; hover to explain.
- Damage numbers use current Strength and are marked approximate.

**Settings:** always-on vs. hover · moves ahead.

**Game UI touched:** enemy intent area.

**Patches:** *to be filled in when implemented.*

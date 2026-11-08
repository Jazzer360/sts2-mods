# Spire QoL: feature reference

This is the complete list of every change Spire QoL makes to the game. If a change isn't here, the mod doesn't make it. Each feature lists what you see, its settings, which game UI it touches, and the game methods it patches.

**Status key:** 🟢 shipped · 🟡 built, being tested in game · ⚪ planned

Spire QoL is **display-only**. It never changes game state, damage, cards or saves. Its manifest sets `"affects_gameplay": false`, so you can join multiplayer lobbies whose players don't have it. See [MODDING_NOTES.md](MODDING_NOTES.md).

**Harmony patches: none.** Every feature works by reading live game state once per frame and drawing its own nodes. Nothing in the game's code is replaced or changed.

| Feature | Id | Status | Default |
| --- | --- | --- | --- |
| [Settings and layout](#settings-and-layout) | (core) | 🟡 | always on |
| [Card Indicators](#card-indicators) | `card-indicators` | 🟡 | on |
| [Relic Counters](#relic-counters) | `relic-counters` | 🟡 | on |
| [Relic Reminders](#relic-reminders) | `relic-reminders` | 🟡 | on |
| [Block Forecast](#block-forecast) | `block-forecast` | 🟡 | on |
| [Incoming Damage](#incoming-damage) | `incoming-damage` | 🟡 | on |
| [Shuffle Warning](#shuffle-warning) | `shuffle-warning` | 🟡 | on |
| [Intent Forecast](#intent-forecast) | `intent-forecast` | ⚪ | on (does nothing yet) |

## What the mod adds to the game, technically

- **One CanvasLayer** (`SpireQoL_Overlay`, layer 10) under the scene tree root. It holds the floating widgets and a toast message.
- **One `SceneTree.ProcessFrame` callback.** It runs each feature, then draws card decorations.
- **A child node `SpireQoL_CardOverlay`** under `%CardContainer` of each card node in your hand that has something to show. While a decorated card is in your hand, Spire QoL changes that container's `Modulate` alpha (Ethereal translucency). The alpha is restored as soon as the node stops showing that card. The game reuses card nodes, so this matters.
- **A subscription to `CombatManager.CombatSetUp`,** to know the current combat.
- **One file**, `mods/SpireQoL/SpireQoL.settings.cfg` (JSON). Never written to game saves.

---

## Settings and layout

**What you see:** floating indicators you can place yourself, and two hotkeys.

- **F8: unlock/lock layout.** When unlocked, every floating indicator shows with an orange outline, even if it has nothing to show right now:
  - **Drag** to move it.
  - **Mouse wheel** over it to scale it (0.4×–3×).
  - **Right-click** to reset it to its default place and size.
  - On drop, it re-anchors to the nearest of nine screen points (corners, edge centers, center). The position is saved relative to that point, so it survives resolution changes. Indicators can't be dragged off screen.
  - Locking makes them click-through again and saves the layout.
- **F9: hide/show all overlays.**
- A short message in the top-left confirms each hotkey.

**Settings file** (`SpireQoL.settings.cfg`, edit while the game is closed):

| Key | Default | Meaning |
| --- | --- | --- |
| `Features` | all `true` | turn features on/off by id |
| `Relics` | (empty) | relic class name → `false` to hide it from counters and reminders, e.g. `"PenNib": false` |
| `Widgets` | defaults | saved positions (`Anchor`, `OffsetX/Y`, `Scale`, `Opacity`) |
| `UnlockLayoutKey` / `HideAllKey` | `F8` / `F9` | Godot key names |
| `EtherealOpacity` | `0.7` | 0.25–1 |
| `ShowInnateEternalBadges` | `false` | |
| `DimBlockedCards` | `true` | |
| `PulseOneStepEarly` | `false` | Relic Counters also flash two steps before firing |
| `FlashHz` | `2.5` | flash speed |
| `ShuffleHandMarkers` | `true` | KEPT badges |

**Not yet:** an in-game settings menu (for now, use the file), anchoring to game elements (health bar, hand), grid snap, per-widget opacity control in game.

---

## Card Indicators

**What you see:** mid-combat changes to a card in your hand, which the game otherwise shows only as text. Badges sit in a row just *above* the card's top edge, so they never cover the cost, title, enchantment tab or affliction overlay.

- **Ethereal cards are translucent** (70% by default) whenever they're Ethereal right now, whatever the source: the card itself, Music Box copies, or Hex (which grants Ethereal through a power).
- **Keyword badges:**
  - `ETH` Ethereal
  - `RET` Retain; `RET·1` retained this turn only (e.g. Expertise)
  - `SLY` Sly; `SLY·1` Sly this turn only (Hand Trick)
  - `+EXH` Exhaust that was *added* (cards that always exhaust get no badge)
  - `INN` / `ETR` Innate / Eternal, off by default
- **Blocked cards are dimmed** with the reason: `BOUND`, `RINGING`, `SMOG`, `CARD LIMIT` (Velvet Choker). Detected with the game's own `CardModel.CanPlay`.
- **Galvanized** cards show `−N HP` (the damage playing them deals you).

Colors avoid the game's glows (cyan/gold/red) and cost colors (blue/green/red).

**Game UI touched:** card nodes in the hand (see "technically" above).

**Not yet:** free-this-turn / free-this-combat / temporary-cost duration markers, replay count, "plays twice" markers, decorations outside the hand (draw/discard views).

---

## Relic Counters

**What you see:** a compact tray (by default bottom-center, above your hand) with one row per counter relic you own: the relic's icon, a bar split into one segment per step, and a short label.

- **Counters that carry across turns and combats sort first:** Pen Nib, Nunchaku, Tuning Fork, Iron Club, Galactic Dust, Joss Paper, Stone Calendar, Happy Flower, Fake Happy Flower, Pendulum, Pollinous Core, Metronome.
- **Then per-turn counters:** Kunai, Shuriken, Ornamental Fan, Kusarigama, Letter Opener, Brilliant Scarf.
- **The row flashes when the next qualifying action fires the relic.** This uses the game's own "about to fire" state (`RelicModel.Status == Active`). Pen Nib flashes at 9/10. Stone Calendar flashes all of turn 7.
- **Limits:**
  - Velvet Choker shows plays left and turns red at 1.
  - Pocketwatch shows green while you're within its card limit and red ("missed") once you go over.
- **Turn-trigger relics** show progress to their turn and say "next turn" the turn before: Horn Cleat, Candelabra (turn 2); Captain's Wheel, Chandelier, Sparkling Rouge (turn 3).
- **One-shot relics** show one segment: green "ready" while unused, "used" afterwards. Vambrace, Throwing Axe, Burning Sticks, Unsettling Lamp, Pael's Eye, Permafrost (per combat); Music Box (per turn).

Thresholds are read live from each relic, so balance patches are picked up automatically.

**Game UI touched:** none; the tray is our own overlay. The relic bar's own counters are unchanged.

**Not yet:** Pael's Legion, Mini Regent, Regalite, Rainbow Ring rows.

---

## Relic Reminders

**What you see:** the relic's own icon on the cards in your hand that it cares about, in the same row above the card as the badges.

- **Crossed-out icon = playing this card stops this relic working this turn.** Art of War and Ripple Basin on every attack, until you've played an attack this turn.
- **Pen Nib ready:** attacks get a pulsing violet border plus the Pen Nib icon. This attack will use the double damage.
- **One-shot relics, while still unused:**
  - Vambrace on cards that give block
  - Throwing Axe on every card
  - Burning Sticks on skills that will exhaust (Exhaust skills, plus Ethereal skills that exhaust at end of turn)
  - Unsettling Lamp on cards that apply a debuff
  - Permafrost on powers
  - Music Box on attacks (first attack each turn)

**Game UI touched:** card nodes in the hand.

**Not yet:** stacking several icons into one with a count, Pael's Eye "end turn = extra turn" hint, Rainbow Ring, the condition relics in TRACKER §3 beyond the ones above.

---

## Block Forecast

**What you see** (default: left side of the screen): `Block 5  +6 at end of turn` with a breakdown, so you stop overblocking. Shown only during your turn when something will add block.

Counts:
- Plating
- Frost orb passives (times the game's passive trigger count, so trigger-boosting effects are included)
- Orichalcum / Fake Orichalcum (only if you currently have 0 block, like the game checks)
- Cloak Clasp (block × cards in hand)
- Ripple Basin (only if you haven't played an attack this turn)
- Horn Cleat / Captain's Wheel are listed as "next turn" the turn before they fire

**Game UI touched:** none; our own widget.

**Not yet:** Sturdy Clamp carry-over, Parrying Shield threshold, placement next to the health bar.

---

## Incoming Damage

**What you see** (default: left side, above Block Forecast), during your turn:
- `Incoming 18`: total blockable damage this round.
- `vs block 5+6 → take 7` in red, or `→ fully blocked (+3 spare)` in green. This counts your current block plus the Block Forecast.
- A breakdown: each attacking enemy (with hit count) and each self-damage source.
- `Unblockable HP loss N`, listed separately and never mixed into the total.

Sources:
- **Blockable:** enemy attack intents, using the game's own intent numbers (`AttackIntent.GetTotalDamage`, so Strength, Vulnerable etc. are included); Burn, Decay, Infection, Toxic, Wither in hand; the Constrict, Magic Bomb, Disintegration debuffs.
- **Unblockable:** Regret (HP loss = cards in hand), Bad Luck, Beckon, Demise.

**Game UI touched:** none; our own widget.

**Not yet:** health-bar preview, per-player targeting in multiplayer (all attack intents are counted).

---

## Shuffle Warning

**What you see** (default: bottom-left, near the draw pile), during your turn when your draw pile is low (5 or fewer) or a shuffle is possible:
- `Reshuffle after N more draws` (or `Next draw reshuffles`), plus draw and discard pile sizes.
- `SHUFFLE` badge on draw cards in hand whose draw is more than your draw pile.
- Potions that would cause a shuffle, listed in the widget.
- `KEPT` badge on your other hand cards while a mid-turn shuffle is possible: they'll stay out of the new draw pile.

Draw cards and potions are a fixed list from the decompile (TRACKER §6).

**Game UI touched:** card nodes in the hand.

**Not yet:** cards whose draw amount isn't a simple number (Compile Driver, Dagger Throw, Escape Plan, Mad Science, Pillage, Prepared, Restlessness, Scrawl), badges on potion slots.

---

## Intent Forecast

⚪ Planned. Needs research into monster AI first (TRACKER §7). Registered but does nothing.

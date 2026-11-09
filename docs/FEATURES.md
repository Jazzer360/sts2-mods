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
| [Incoming Damage](#incoming-damage) | `incoming-damage` | 🟡 | on |
| [Shuffle Warning](#shuffle-warning) | `shuffle-warning` | 🟡 | on |
| [Intent Forecast](#intent-forecast) | `intent-forecast` | ⚪ | on (does nothing yet) |

## What the mod adds to the game, technically

- **One CanvasLayer** (`SpireQoL_Overlay`, layer 10) under the scene tree root, holding a toast message and a `SpireQoL_Widgets` container for the floating widgets. **During live combat with no screen open, that container is moved inside the game's `NCombatUi`, directly before the `%Hand` node, so your cards draw over the widgets.** It moves back to our layer when combat ends, when any screen opens over combat, and while the layout is unlocked (so the hand can't swallow drag clicks). Widgets attached to a game element read that element's screen position each frame; they never modify it.
- **Floating widgets hide while a game screen covers combat:** the pause menu, draw/discard/exhaust pile and deck views (`NCapstoneContainer.InUse`), card rewards and card-choice screens (`NOverlayStack.ScreenCount > 0`), and the map (`NMapScreen.IsOpen`). Card decorations live on the card nodes in your hand, so they already sit underneath those screens.
- **One `SceneTree.ProcessFrame` callback.** It runs each feature, then draws card decorations.
- **A child node `SpireQoL_CardOverlay`** under `%CardContainer` of each card node in your hand that has something to show. While a decorated card is in your hand, Spire QoL changes that container's `Modulate` alpha (Ethereal translucency). The alpha is restored as soon as the node stops showing that card. The game reuses card nodes, so this matters.
- **A subscription to `CombatManager.CombatSetUp`,** to know the current combat.
- **One file**, `mods/SpireQoL/SpireQoL.settings.cfg` (JSON). Never written to game saves.

---

## Settings and layout

**What you see:** floating indicators you can place yourself, and two hotkeys.

- **F8: unlock/lock layout.** When unlocked, every floating indicator shows with an orange outline, even if it has nothing to show right now, plus a header with:
  - **A 3×3 pin picker.** It chooses which corner or edge of the widget stays fixed when the widget grows or shrinks. With the bottom-left pin, for example, extra counter rows grow the widget upward and to the right. Picking a pin never moves the widget. The orange square is the current pin.
  - **An Attach button** (widgets that can follow a game element: Incoming Damage, Relic Counters). When attached, the widget follows your character. Otherwise it stays fixed on screen.
- While unlocked:
  - **Drag** to move a widget. It keeps your chosen pin; there's no automatic re-anchoring.
  - **Mouse wheel** over it to scale it (0.4×–3×).
  - **Right-click** to reset it to its default place, pin and size.
  - **Middle-click** is the same as the Attach button.
  - **Ctrl+1–9** while hovering a widget is a shortcut for the pin picker, laid out like a numpad (7 8 9 top, 4 5 6 middle, 1 2 3 bottom). Ctrl avoids the game's number-key card shortcuts.
- **How positions are stored:**
  - **On screen:** the pin is measured from the matching point of the screen. A bottom-left pin is measured from the screen's bottom-left corner.
  - **Attached:** the pin is measured from a point beside the game element. Relic Counters uses just right of the HP bar's bottom-right corner, with a natural bottom-left pin. Incoming Damage uses just left of the block icon, with a natural right pin.
  - Either way, positions survive resolution changes, and widgets can't go off screen.
- Locking makes the widgets click-through again and saves the layout.
- **F9: hide/show all overlays.**
- A short message in the top-left confirms each hotkey.

**Settings file** (`SpireQoL.settings.cfg`, edit while the game is closed):

| Key | Default | Meaning |
| --- | --- | --- |
| `Features` | all `true` | turn features on/off by id |
| `Relics` | (empty) | relic class name → `false` to hide it from counters and reminders, e.g. `"PenNib": false` |
| `Widgets` | defaults | saved positions (`Anchor` = pin, `OffsetX/Y`, `Scale`, `Opacity`, `FollowGame`, `CustomPin`) |
| `LayoutVersion` | (managed) | when a release changes a default position, older saved positions for that widget are reset once |
| `UnlockLayoutKey` / `HideAllKey` | `F8` / `F9` | Godot key names |
| `EtherealOpacity` | `0.8` | 0.25–1 (a saved 0.7, the old default, is raised to 0.8 once) |
| `ShowInnateEternalBadges` | `false` | Innate / Eternal tabs |
| `DimBlockedCards` | `true` | |
| `ShowDoublePlay` | `true` | Echo Form tab on cards that play more than once |
| `PulseOneStepEarly` | `false` | Relic Counters also flash two steps before firing |
| `FlashHz` | `2.5` | flash speed |

**Not yet:** an in-game settings menu (for now, use the file), attaching to game elements other than the player's HP (hand, draw pile), grid snap, per-widget opacity control in game.

---

## On-card tabs: one language for everything on a card

Every indicator about a specific card in your hand uses the **same form**: a **tag** hanging up off the card's **top edge**, centered in a row, one tag per effect. In a fanned hand the tops of cards stay visible while their sides are covered, so you can read the tags at a glance without hovering. A tag means something out of the ordinary will happen when you play that card.

- **Look:** each tag is a copy of the card's own enchantment tab (the game's banner art), rotated a quarter turn so it hangs off the top edge the way an enchantment tab hangs off the side. **It draws beneath the card art but above the card's glow**, tucked about 17% under the top edge, so the card itself covers the tucked part. The icon is drawn slightly smaller (85%), centered as if the tag were tucked 25% (the backdrop sits a little lower than the icon, evening out the margins above and below it). Each tag carries the icon of whatever is affecting the card: a relic, power or enchantment, or the draw pile.
- **Icon only:** no numbers on tags. The icon says what's going on; you know the details.
- **Red slash:** the icon crossed out means playing this card breaks or is blocked by that thing.
- **One per source:** each source shows at most one tab per card. If a card has many, the row shrinks to fit the card's width.
- **Not tabs:** the only on-card effects that aren't tabs are **dimming** (the card can't be played right now; the reason is a crossed tab) and **Ethereal translucency**.

| Tab icon | Meaning | Feature |
| --- | --- | --- |
| Steady (enchantment) | Retain (permanent or this turn only) | Card Indicators |
| Master Planner (power) | Sly (permanent or this turn only) | Card Indicators |
| Echo Form (power) | plays more than once (any source: Echo Form, Throwing Axe, Burst...) | Card Indicators |
| Goopy (enchantment) | Exhaust was *added* to this card | Card Indicators |
| Royally Approved / Tezcatara's Ember (enchantments) | Innate / Eternal (off by default) | Card Indicators |
| Galvanic (power) | Galvanized: playing it damages you | Card Indicators |
| Bound / Ringing / Smog power, or Velvet Choker, **crossed** + card dimmed | can't be played right now | Card Indicators |
| Discard pile | its draw would reshuffle your discard pile into the draw pile | Shuffle Warning |
| A relic | playing this uses the relic (Pen Nib ready, one-shot relics) | Relic Reminders |
| A relic, **crossed** | playing this stops the relic working (Art of War, Ripple Basin, Pael's Tears) | Relic Reminders |

**Game UI touched:** card nodes in the hand. Tag copies live in a `SpireQoL_TagLayer` node inserted directly after the card's glow (`NCard.CardHighlight`) in the glow's parent, so they draw above the glow and beneath the card art. It mirrors the card frame's transform each frame, and is hidden when the node stops showing a decorated hand card. The game's own enchantment tab is only read as a template.

---

## Card Indicators

**What you see:** mid-combat changes to a card in your hand that the game otherwise shows only as text, as on-card tabs (see above).

- **Ethereal cards are translucent** (80% opacity by default) when Ethereal is on the card itself: cards that are always Ethereal, and Music Box copies. **Not** when Ethereal comes from Hex (the act 3 elite's Hexed affliction): Hex makes every card Ethereal through a power (a "global" keyword source), and the game's affliction overlay already marks those cards.
- **Retain:** a Steady tab (Steady is the game's Retain enchantment). **Sly:** a Master Planner tab (the de-facto Sly power). Granted-for-this-turn (Expertise, Hand Trick) looks the same.
- **Added Exhaust:** a Goopy tab (Goopy is the enchantment that adds Exhaust). Cards that always exhaust get nothing.
- **Innate / Eternal** (setting `ShowInnateEternalBadges`, off by default): Royally Approved / Tezcatara's Ember tabs, the enchantments that grant them.
- **Blocked cards** (Bound, Ringing, Smog, Velvet Choker): the card is dimmed, with a crossed tab showing the blocking power's or relic's icon. Detected with the game's own `CardModel.CanPlay`.
- **Galvanized:** a Galvanic tab (playing the card damages you).
- **Double play:** an Echo Form tab, whatever the source.
  - The count comes from the game's own play-count query, the one it runs when you play the card: enchanted replays + 1, then `Hook.ModifyCardPlayCount`. So it covers Echo Form (first N cards each turn), Burst (skills), Duplication, One-Two Punch (attacks), Signal Boost (powers), Throwing Axe (first card each combat), Tag Team, enchantments, and anything added later.
  - Tag Team only doubles attacks aimed at the enemy that has it. Single-target cards are checked against each enemy, and the best case is shown.

**Not yet:** free-this-turn / free-this-combat / temporary-cost duration markers, decorations outside the hand (draw/discard views).

---
## Relic Counters

**What you see:** a compact tray, by default attached just right of your HP bar with its bottom edge level with the bar's bottom (so it stays clear of enemies), with one row per counter relic or countdown power you have: its icon, a bar split into one segment per step, and a short label. Counters longer than 12 steps (Cacophony's 33) show only the countdown number, no bar.

- **Counters that carry across turns and combats sort first:** Pen Nib, Nunchaku, Tuning Fork, Iron Club, Galactic Dust, Joss Paper, Stone Calendar, Happy Flower, Fake Happy Flower, Pendulum, Pollinous Core, Metronome.
- **Then per-turn counters:** Kunai, Shuriken, Ornamental Fan, Kusarigama, Letter Opener, Brilliant Scarf.
- **The row flashes when the next qualifying action fires the relic.** This uses the game's own "about to fire" state (`RelicModel.Status == Active`). Pen Nib flashes at 9/10. Stone Calendar flashes all of turn 7.
- **Limits:**
  - Velvet Choker shows plays left and turns red at 1.
  - Pocketwatch shows green while you're within its card limit and red ("missed") once you go over.
- **Turn-trigger relics** show progress to their turn and say "next turn" the turn before: Horn Cleat, Candelabra (turn 2); Captain's Wheel, Chandelier, Sparkling Rouge (turn 3).
- **Pael's Tears** shows green `+2 next turn` while you still have energy (ending your turn with energy left gives energy next turn), and red `no energy left` once you've spent it all.
- **Countdown powers** (sorted after relics; the game's `PowerModel.DisplayAmount` is "steps left"). The bar fills toward the next trigger and flashes when one step is left:
  - Across turns: Orbit (every 4 energy spent), Automation (every 10 cards drawn), Cacophony (every 33 cards drawn, number only).
  - Per turn: Panache (every 5 cards played), Pale Blue Dot (5 cards played, any type; shows `done` once it has fired this turn).
  - Enemy powers that count *your* plays, in red: Withering Presence (every 6 cards you play adds a Wither to your hand).
- **Pael's Eye** shows one segment: green "ready" while unused this combat, "used" afterwards. It's the only one-shot relic in the tray, because it has no card to mark. The other one-shot relics (Vambrace, Throwing Axe, Burning Sticks, Unsettling Lamp, Permafrost, Music Box) appear only as icons on the cards that would use them (Relic Reminders).

Thresholds are read live from each relic, so balance patches are picked up automatically.

**Game UI touched:** none; the tray is our own overlay. The relic bar's own counters are unchanged.

**Not yet:** Pael's Legion, Mini Regent, Regalite, Rainbow Ring rows.

---

## Relic Reminders

**What you see:** a tab with the relic's icon on the cards in your hand that it cares about (see "On-card tabs").

- **Crossed tab = playing this card stops this relic working this turn.**
  - Art of War and Ripple Basin on every attack, until you've played an attack this turn.
  - Pael's Tears on every playable card that would spend your last energy (its current cost ≥ your energy, and all X-cost cards).
- **Plain tab = playing this card uses the relic.**
  - Pen Nib, when ready, on attacks: this attack gets the double damage. The tray row also flashes.
  - One-shot relics, while still unused:
    - Vambrace on cards that give block
    - Throwing Axe on every card
    - Burning Sticks on skills that will exhaust (Exhaust skills, plus Ethereal skills that exhaust at end of turn)
    - Unsettling Lamp on cards that apply a debuff
    - Permafrost on powers
    - Music Box on attacks (first attack each turn)

**Not yet:** Pael's Eye "end turn = extra turn" hint, Rainbow Ring, the condition relics in TRACKER §3 beyond the ones above.

---

## Incoming Damage

**What you see**, during your turn: a small stacked sum, read like a math problem. It uses the game's own intent icons: defend, and the attack icon tier that matches the incoming total (the same thresholds enemies use: under 5, 10, 20, 40, and 40+):

```
[attack]  14     incoming damage
[defend] − 5     your block at end of turn
──────────
      =   9     what you'll lose (red), or 0 in green
```

**Placement:** it sits just left of your block indicator, at the left end of your HP bar, and follows your character. If that doesn't fit on screen (your character stands near the left edge), it drops below the HP bar and every row of power icons instead; wrapped rows are measured. Move it with F8, or middle-click to pin it to the screen instead.

**How the numbers are worked out:** the enemy turn is simulated hit by hit, in turn order:
- **Your end-of-turn self damage** lands first, against your end-of-turn block.
- **Poison:** an enemy that Poison will kill doesn't attack. Poison ticks at the start of its turn, before it moves. The amount is the game's own poison preview (`PoisonPower.CalculateTotalDamageNextTurn`), which already applies per-hit damage caps such as Exoskeleton's **Hard To Kill** (each instance capped, e.g. at 9). Skulking Colony's **Hardened Shell** caps an enemy's *total* HP loss per turn; the game's preview ignores that, so we apply it.
- **Doom doesn't stop attacks.** Doomed enemies die at the end of their own turn, after attacking.
- **Retaliation** can kill an attacker partway through a multi-hit. Each later hit is then cancelled, because the game checks for a dead attacker before every hit.
  - Thorns hits before each blow. The blow during which Thorns kills still lands.
  - Flame Barrier hits after each blow.
  - Reflect, after each blow, deals the damage you blocked.
  - Retaliation damage goes through the game's damage hook, so the enemy's caps and Vulnerable apply. It hits the enemy's block first; enemy block is assumed cleared at its turn start unless it has Barricade.
- **Unblockable HP loss** (Regret, Bad Luck, Beckon, Demise) is added to both the incoming and the result.

**Sources:**
- **Blockable:** enemy attack intents (`AttackIntent.GetSingleDamage` × hits, the same numbers the game shows); Burn, Decay, Infection, Toxic, Wither in hand; the Constrict, Magic Bomb, Disintegration debuffs.
- **Unblockable:** Regret (HP loss = cards in hand), Bad Luck, Beckon, Demise.
- **End-of-turn block** (the "block" line = current block + these):
  - Plating
  - Frost orb passives (times the game's passive trigger count, so trigger-boosting effects are included)
  - Orichalcum / Fake Orichalcum (only if you currently have 0 block, like the game checks)
  - Cloak Clasp (block × cards in hand)
  - Ripple Basin (only if you haven't played an attack this turn)
  - Not counted: Horn Cleat and Captain's Wheel. They give block at the *start* of turn 2/3, after the enemy has attacked; Relic Counters shows their countdown.

**Game UI touched:** none; our own widget.

**Game UI read (not changed):** the player's `NCreature` → `%HealthBar` (`NCreatureStateDisplay`) → its `%HealthBar` (and that bar's `%BlockContainer`) and `%PowerContainer`'s icons, for placement only.

**Not yet:**
- Health-bar preview.
- Per-player targeting in multiplayer (all attack intents are counted).
- Breakdown on hover.
- Enemies that gain block mid-turn before retaliation lands.
- Other end-of-turn effects that could kill enemies first (e.g. Lightning orbs).

---

## Shuffle Warning

**What you see:** a **discard pile** tab (the game's own discard pile icon) on draw cards in your hand whose draw is more than your draw pile, so playing them reshuffles your discard pile mid-turn. Nothing else: you know what a reshuffle means for the cards in your hand.

Draw cards are every `CardPileCmd.Draw` caller in the decompile (TRACKER §6). Most draw their Cards amount; Dagger Throw, Escape Plan and Pillage draw one (Pillage at least one); Scrawl draws up to a full hand; Compile Driver draws one per distinct orb type (the card's own calculation); Restlessness draws only when it's the only card in hand; Mad Science draws only with the Wisdom rider. The discard pile icon is read from the live discard pile button (`NCombatUi.DiscardPile` → `Icon`).

**Not yet:** draw potions (potion slots have no tab yet).

---

## Intent Forecast

⚪ Planned. Needs research into monster AI first (TRACKER §7). Registered but does nothing.

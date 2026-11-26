# Tracker

The master work list for Spire QoL. Check an item off only when it is implemented, documented in [FEATURES.md](FEATURES.md), and noted in [CHANGELOG.md](../CHANGELOG.md).

This file holds what's open. Done items move to [tracker-done.md](tracker-done.md), under the same sections, with `node scripts/tracker-done.mjs` (run it when they pile up): search there for how something was done.

Everything below was found in the decompile of game **v0.111.0**. Class names are the game's own (`MegaCrit.Sts2.Core.Models.*`). Thresholds come from each class's `CanonicalVars`. Re-check them after game patches.

Legend: `[ ]` to do · `[~]` built, needs an in-game check · `[x]` done · **(verify)** behavior inferred from code, confirm in game.

---

## 0. Core: settings and layout (`core/`, used by every feature)

- [~] **Settings storage**: per-feature on/off and options, saved to a JSON file in the mod's own folder (not in game saves). Survives game updates.
- [~] **Movable widgets**: every free-floating indicator (relic counter tray, incoming-damage readout, block forecast, shuffle counter) is a `MovableWidget`.
  - [~] **Unlock layout** toggle (menu and hotkey): widgets show an outline and can be dragged. **Lock** freezes them and makes them click-through.
  - [~] **Anchor** (0.6.0: Ctrl+1–9 picks a screen anchor; HP bar and block icon attach points done; hand/draw pile still to do): each widget pins to a screen corner/edge/center, or to a game element (player health bar, hand, draw pile, relic bar). Positions are stored as anchor + offset, so they survive resolution and window-size changes.
  - [ ] **Snap**: optional grid snap while dragging.
  - [~] **Scale and opacity** per widget.
  - [~] **Reset**: per widget and "reset all" to defaults.
  - [~] Off-screen guard: a widget dragged or loaded outside the visible area is pulled back on screen.
- ~~On-card indicator slot as a setting~~: dropped. Every on-card indicator is a tab along the card's top edge (see CLAUDE.md UI rules).
- [~] **Hide all overlays** hotkey.

---

## 1. Card modifiers → Keyword Tags (`card-indicators`) and Ethereal Fade (`ethereal-fade`)

### Keywords
The game shows these only as card text. `CardModel.Keywords` merges local keywords (on the card) with global ones (granted by powers), so a single read catches both.

- [~] **Ethereal translucency**: any card that is Ethereal right now renders at reduced opacity, whatever the source.
  - Sources: canonical Ethereal cards; Music Box (adds Ethereal to its copy); Hexed affliction via `HexPower` (granted globally, gone when Hex is removed).
  - [x] Ascender's Bane drew its art white in the whole-card fade (1.21.3–1.21.4), and 1.21.5 faded every Ethereal card layer by layer; the whole-card fade was taken out in 1.21.6 (decision #112). From 1.21.7 a faded card is a flattened copy drawn as one picture (decision #113), confirmed clean in game.
  - Must still be readable. Opacity is a setting (default ~70%).
- ~~Ethereal badge~~: dropped in 0.3.0; translucency alone marks Ethereal.
- [~] **Retain tab** (0.9.0: Steady icon on a copy of the enchantment tab; retain for this turn only looks the same): from the Retain keyword or single-turn retain (`ShouldRetainThisTurn`).
  - Adds Retain: Anointed, Calculated Gamble, Dredge, Gold Axe, Misery, Monologue, Reaper Form, Royal Gamble, Scrawl, Times Up, Wish, Wisp; enchantments Royally Approved and Steady.
  - Single-turn retain: Expertise.
- [~] **Sly tab** (0.9.0: Master Planner icon on a copy of the enchantment tab): from the Sly keyword or single-turn Sly (`IsSlyThisTurn`; Hand Trick).
- ~~**Exhaust badge**~~ (added Exhaust, Goopy): removed. Added keywords are in the card text, so they don't get a tab.
- ~~**Innate / Eternal badges**~~: removed in 0.19.0. Neither can be added or removed mid-combat, so they don't say anything about playing the card now.
- ~~**Keyword removed** marker~~: dropped (2026-11-09). The change is in the card text, and tabs don't repeat the card text.

### Cost and play-count changes
The game already recolors changed costs (blue = increased, green = decreased, red = can't afford), so cost changes get no tab. Only extra plays do.

- ~~**"Free this turn"** marker~~: dropped (2026-11-09). The game already shows the new cost, and a tab only answers "does something happen when I play this?"
- ~~**"Free this combat"** marker~~: dropped (2026-11-09), same reason.
- ~~**Temporary cost change** glyph~~: dropped (2026-11-09). Not a tab, and the game already recolors changed costs.
- ~~**Global cost modifiers**~~: dropped (2026-11-09). The game already shows the modified cost on each card.
- [~] **Replay count** (0.10.0: covered by the double-play tab, via the game's own play-count query). Sources: Hidden Gem, Soldier's Stew, Sword Sage power, Transfigure.
- [~] **"Plays twice" next play** (0.10.0, via `Hook.ModifyCardPlayCount`; 0.18.0: the tab shows the source's own icon): Burst, Duplication, Echo Form, One-Two Punch, Signal Boost, Tag Team powers. Mark the cards in hand this applies to. (Throwing Axe is handled under one-shot relics, §3.)

### Afflictions (monster-applied)
All seven already have game overlays (`AfflictionModel.OverlayPath`). Our job is to stay out of their way. The game also marks cards that can't be played (crossed-out cost), so there's no "can't play" tab (removed in 0.23.1).

- [~] **Hexed** (Hex): grants Ethereal → handled by translucency. Confirm the overlay and translucency look right together.
- [~] **Galvanized** (Galvanic): playing the card deals N damage to you. Crossed Galvanic tab (Proc Indicators). Galvanic read from its code (decision #221).
- [~] **Tainted**: playing a Tainted card triggers Vital Spark. Crossed Vital Spark tab (Proc Indicators, 0.19.0).

### Enchantments (run-long, already have a game tab)
Shown by the game's `%Enchantment` tab, so no new indicator. Only make sure our tabs don't overlap it.

---

## 2. Counter relics → `relic-counters` (tick-bar tray above the hand)

Every relic here exposes `ShowCounter`, `DisplayAmount` and `Status` (the game sets `Active` one step before it fires). Each relic gets one condensed bar: icon plus tick-marked segments that fill as the counter advances. The bar **flashes** when the next qualifying card fires it. Counters that carry across turns or combats are always shown and sort first.

| Done | Relic | Counts | Fires at | Resets | Effect |
| --- | --- | --- | --- | --- | --- |
| [~] | **Pen Nib** | attacks | 10th | never (carries across combats) | next attack deals double. Pen Nib tab on attacks in hand when ready (§3) |
| [~] | Nunchaku | attacks | 10th | never | +1 energy |
| [~] | Tuning Fork | skills | 10th | never | gain block |
| [~] | Iron Club | any card | 4th | never | draw |
| [~] | Kunai | attacks | 3rd | each turn | +Dexterity |
| [~] | Shuriken | attacks | 3rd | each turn | +Strength |
| [~] | Ornamental Fan | attacks | 3rd | each turn | gain block |
| [~] | Kusarigama | attacks | 3rd | each turn | deal damage |
| [~] | Letter Opener | skills | 3rd | each turn | deal damage |
| [~] | Brilliant Scarf | any card | 5th | each turn | that card is free |
| [~] | Velvet Choker | any card | limit 6 | each turn | **inverse**: show plays left; warn at 5 |
| [~] | Pocketwatch | any card | ≤3 played | each turn | **inverse**: stays lit while ≤3; play a 4th and next turn's extra draw is lost |
| [~] | Joss Paper | exhausts | 5th | never (carries across combats; Ethereal exhausts count at end of turn) | draw |
| [~] | Galactic Dust | stars spent | 10 | never (saved across combats) | gain block |
| [~] | Metronome | orbs channeled | 7th | each combat | deal damage |
| [~] | Pael's Legion | cooldown (turns) | 2 | after use | double block on the next block card. 0.23.4: no tray row (the game shows the cooldown on the relic); tab on block cards while off cooldown, like Vambrace |

### Countdown powers (same tray; the game's `PowerModel.DisplayAmount` is "steps left")

| Done | Power | Counts | Fires at | Resets | Effect |
| --- | --- | --- | --- | --- | --- |
| [~] | Orbit | energy spent | 4 | never | gain energy |
| [~] | Automation | cards drawn | 10 | never | gain energy |
| [~] | Cacophony | cards drawn | 33 | never | damage a random enemy (number only, no bar) |
| [~] | Panache | cards played | 5 | each turn | damage all enemies |
| [~] | Pale Blue Dot | cards played (any type) | 5 | each turn (once) | draw next turn |
| [~] | Withering Presence (enemy) | your cards played | 6 | never | Wither added to your hand |
| — | Feral, Monologue, Sloth, Tender, Slow, Tag Team | | | | custom display, but not a countdown to an effect; skipped |

### Energy carry-over

| Done | Relic | Display |
| --- | --- | --- |
| [x] | Pael's Tears | tray: `+2 next turn` while energy > 0. Cards: crossed icon on playable cards whose cost ≥ current energy (X-cost included) |

### Turn-counter relics (same tray, counting turns)

| Done | Relic | Fires | Effect |
| --- | --- | --- | --- |
| [~] | **Stone Calendar** | end of turn 7 | deal damage. Pips for turns 1–7; blink all of turn 7 ("fires when you end this turn") |
| [~] | Happy Flower | every 3 turns | +1 energy |
| [~] | Fake Happy Flower | every 5 turns | +1 energy |
| [~] | Pendulum | every 3 turns | extra draw |
| [~] | Pollinous Core | every 4 turns | +2 draw |
| [~] | Horn Cleat | turn 2 | gain block (start of turn, after enemies attack: not counted as end-of-turn block) |
| [~] | Captain's Wheel | turn 3 | gain block (start of turn, after enemies attack: not counted as end-of-turn block) |
| [~] | Candelabra | turn 2 | +2 energy |
| [~] | Chandelier | turn 3 | +3 energy |
| [~] | Sparkling Rouge | turn 3 | +Strength/+Dexterity |

---

## 3. Condition relics → `proc-indicators` (Proc Tags: tags on cards in hand; Relic Reminders was merged into it)

| Done | Relic | Show on | Meaning |
| --- | --- | --- | --- |
| [~] | **Pen Nib (ready)** | attacks | Pen Nib tab: "this attack uses Pen Nib" |
| [~] | Art of War | attacks | struck-through icon while no attack played this turn (playing one loses next turn's energy) |
| [~] | Ripple Basin | attacks | struck-through icon: playing an attack loses the end-of-turn block |
| [~] | Rainbow Ring | the card that completes the set | tab on a card whose type completes attack + skill + power this turn |
| [~] | Mummified Hand | powers, only when another hand card still costs something | playing a power makes a random card in hand free (0.23.3: no tab when nothing in hand costs anything) |
| [~] | Game Piece | powers | draw on power play |
| [~] | Lost Wisp | powers | damage on power play |
| [~] | Daughter of the Wind | attacks | block per attack |
| [~] | Ivory Tile | cards costing ≥3 | refund energy |
| [~] | Helical Dart | Shivs | +Dexterity |
| [~] | Razor Tooth | attacks and skills that can still be upgraded | upgrades the card when played; a replay's later plays are the upgraded card's (decision #218) |
| [~] | Chemical X | X-cost cards | +2 to X |
| — | Strike Dummy / Fake Strike Dummy | Strikes | not a tab: it's a damage modifier (`ModifyDamageAdditive`), so the card's own damage number already includes it |
| — | Velvet Choker | | no card tab since 0.23.1: the tracker shows plays left, and the game crosses out the cost once you can't play more |
| [~] | Cloak Clasp | hand | end-of-turn block per card left in hand (counted in `incoming-damage`) |
| [~] | Screaming Flagon | hand | damage at end of turn if your hand is empty: counted by Incoming Damage (end-of-turn effects) |
| [~] | Tingsha, Tough Bandages | discard | per card discarded on your turn: 3 damage to a random enemy / 3 block |
| [~] | Charon's Ashes, Forgotten Soul | exhaust | per card exhausted: 3 damage to all enemies / 1 damage to a random enemy |

### One-shot relics (first time each combat / each turn)

These relics are "armed" until their first use, then go quiet. The game marks them `Status = Active` while armed. Spire QoL shows:
- **On cards:** the relic's icon on every card in hand that would use it, *only while it's still armed*. The icons disappear the moment it's spent.
- **In the relic tray:** a single lit segment while armed, dark once used. They share the tray with the counter relics.

**Once per combat** (re-arms when combat starts):

| Done | Relic | Cards marked | Effect, from the code |
| --- | --- | --- | --- |
| [~] | **Vambrace** | cards that grant block | the first card that grants you block this combat gives double block. Only block from that one card is doubled |
| [~] | **Throwing Axe** | every card in hand | the first card you play this combat is played twice. Any type, so the marker should help you choose the card |
| [~] | **Burning Sticks** | skills that will exhaust: Exhaust skills (including added Exhaust), plus Ethereal skills at end of turn | the first skill exhausted this combat returns a copy to your hand. Exhaust from Ethereal counts too (`causedByEthereal` is accepted) |
| [x] | Unsettling Lamp | cards that apply a debuff to an enemy | the first card that debuffs an enemy this combat applies double the debuff. Doesn't trigger or get spent against a target with Artifact. Note that on the card |
| [x] | Permafrost | powers | the first power played this combat gives block |
| [~] | Pael's Eye | (hand-wide) | once per combat, ending a turn with no cards played exhausts your hand and gives an extra turn. While armed, show "end turn now = extra turn" if you haven't played a card yet |

**Once per turn** (re-arms each turn):

| Done | Relic | Cards marked | Effect, from the code |
| --- | --- | --- | --- |
| [~] | Music Box | attacks | the first attack each turn adds an Ethereal copy to your hand |
| [~] | Rainbow Ring | missing card types | playing an attack, a skill and a power in the same turn gives +Strength/+Dexterity (once per turn) |
| [~] | Mini Regent | (stars) | the first star spend each turn gives +1 Strength (`UsedThisTurn`, reset at your turn start) |
| [~] | Regalite | (generated cards) | 4 block the first time you create a card each turn (`UsedThisTurn`) |

---

## 4. End-of-turn block sources → `incoming-damage` (Block Forecast widget removed in 0.13.0)

Shows "+N at end of turn" beside current block, so you don't overblock.

- [~] **Plating** (power): gain N block early in your end of turn (`BeforeSideTurnEndEarly`, before end-of-turn damage); the stack drops by 1 at your turn start (not turn 1).
- [~] **Frost orb passive**: each Frost gives `PassiveVal` block, times the passive trigger count (`Hook.ModifyOrbPassiveTriggerCount`).
- [~] **Orichalcum / Fake Orichalcum**: block if you have none at end of turn.
- [~] **Cloak Clasp**: block × cards in hand.
- [~] **Ripple Basin**: block if no attack played this turn.
- ~~**Horn Cleat / Captain's Wheel**~~: not end-of-turn block (they fire at the start of turn 2/3, after enemies attack). Countdown lives in Relic Counters.
- [~] **Feel No Pain + Ethereal** (gap found 2026-11-09, counted in 0.19.0): Ethereal cards left in hand exhaust at end of turn, and Feel No Pain gives `Amount` block per exhaust (Unpowered, so no Dexterity). That block lands before the enemy attacks but isn't counted yet. Same for **Dark Embrace**-style exhaust procs that give block (see §9).
- [~] **Rage / Skittish**: card tabs (Proc Indicators): Rage gives you block on attacks; Skittish (enemy) is crossed, it gains block when hit. Not in the end-of-turn total.
- — **Sturdy Clamp** (keeps up to 10 block into your next turn): nothing to add. Incoming Damage reads your real block each turn, which includes what it kept, and the mod doesn't show wasted block. A "kept for next turn" readout would be a new display.
- [~] **Parrying Shield**: counted by Incoming Damage when you'll end with enough block. With several enemies its target is random and isn't counted yet (§10).

---

## 5. Incoming damage → `incoming-damage`

**Blockable** damage goes in the total. **Unblockable** HP loss is listed separately and never mixed in.

- [~] **Enemy attack intents**: sum `AttackIntent.GetTotalDamage(targets, owner)` across enemies (multi-hit included). Multiplayer: count only intents that target you.
- [~] **Burn** (in hand, end of turn): blockable.
- [~] **Decay**: blockable.
- [~] **Toxic**: blockable.
- [~] **Wither**: blockable.
- [~] Unblockable, listed separately: Regret (HP loss = cards in hand), Bad Luck, Beckon, Demise. Poison is already previewed by the game.
- [ ] Not damage, but worth a reminder: Doubt (Weak), Shame (Frail), Debt (lose gold).
- [~] **Doesn't count enemies Poison kills before they act** (with Hard To Kill per-hit cap and Hardened Shell per-turn cap). Doom does NOT prevent attacks.
- [~] **Retaliation cancels remaining hits**: Thorns (before each hit; the killing hit still lands), Flame Barrier and Reflect (after each hit), through the game's damage hook.
- [ ] **Health-bar preview** (the summary line now sits under the HP bar; a bar overlay is still open): render the expected loss after block alongside the game's existing Poison/Doom preview in `NHealthBar`.

---

## 6. Shuffle Tag → `shuffle-warning`

- ~~Draw-pile counter~~: dropped in 0.3.0 (the card badges are enough).
- [~] Discard pile tab on draw cards in hand whose draw would exceed the draw pile (forcing a reshuffle).
- ~~KEPT marker on other hand cards~~: dropped in 0.12.0 (the draw-pile tab is enough).
- [ ] Draw potions: a shuffle tab under potions whose draws reshuffle (potion tabs exist since 0.41.0).

---

## 7. Intent Forecast (built in 1.12.0)

Knowable information only (decision #72): an enemy's next moves where its move rules decide them (a fixed pattern), and the odds of each possible next intent when the pick is random, worked out from its move rules and the moves you've seen it make. Never the result of a random pick before the game shows it, a later move the game rolled ahead of time, or the RNG.

Design (decision #72): on hover only; looks ahead until a roll (every certain turn, then the first random pick with each move's chance).
- [ ] Test in game: the hover panel (shown on hovering the intent, stacked above it (1.13.1), NIntent nodes' size and animation, the chance labels), a stunned enemy, a hidden intent.
- [ ] Test in game: the forecast's later attacks against what they hit for (a Cultist's Ritual, an enemy's Weak wearing off, Vulnerable on you), the sword's size and the label; the audit's `[audit] [intent] projection:` lines; how long a big fight's projection takes on its worker before the later attacks take their numbers (decision #308: about 1.3 ms for the big board in a debug test run, 89 copies; 0.7 s and 24,225 before), and the audit's "still on its way" skips.
- [ ] Your next turn's start's listeners not followed (decision #306: each makes that projected turn unknown, `turn-start-gaps.json`): AfterBlockCleared, no read model's hook yet (Block Next Turn, Self-Forming Clay, Toric Toughness, Captain's Wheel, Horn Cleat, Sparkling Rouge); the AutoPrePlay phase (Mayhem's and History Course's plays: unknown by nature, a card from the draw pile); commands not translated there (PlayerCmd.LoseEnergy: Amnesia, Bread; a card made in hand: Blessed Antler's Dazed, Ennui's Sadness; ForgeCmd.Forge: Furnace; a pick: Tools of the Trade, Tyranny; a loop: Aggression, Rampart, Emotion Chip's orbs; Coolant's count of orb kinds; Countdown's random Doom target; Pael's Flesh's and Pendulum's Status; Booming Conch's room); partly read classes (Blur, Clarity, Pollinous Core, Sloth); hand-written models that don't answer it (Feral, Pael's Tears, Pocketwatch). Since decision #308 (the owner's call), one whose acting hooks call only commands on hands, cards, energy, HP or block, or a power's command on itself when it changes none of the forecast's math, no longer makes the turn unknown (`ForecastIgnores`: 25 of the 29). Still unknown: Countdown's random Doom, Mayhem's and History Course's plays, Sparkling Rouge.
- [ ] Draws in a projected turn (decision #306): your hand's draw and any draw at your next turn's start give nothing the projection follows (`SimState.HandLeftOut`), so a listener on a draw (AfterCardDrawn: a power that hits or blocks per card drawn) isn't told and marks nothing. Mark a projected turn unknown where one is on the board, or follow the draws' count with the cards unknown.
- [ ] An attack intent's damage that reads its own power in another shape than a sum with one such term (a product, a power of another creature's, a getter with a condition), or that adds one in the current turn's enemy hits (the projection's only, decision #344): read as it stands now, as before. No monster's damage has such a shape today besides The Forgotten's (which reads); a scan over the attack intents' getters would catch the next one.
- [ ] The projection's "only what's certain" probe (decision #308: `RollChangesItsHits`) checks an enemy's damage math on you and its stun where a roll left the branches apart; a power that changes its numbers only later (a Ritual a roll gave some branches) is collapsed to what it was without naming the enemy uncertain. The audit catches it as a plain mismatch.
- [ ] The projection's displayed number (as it lands: after your next turn's end, the enemies' turn start and the move's own steps) is held only through its turn-start reading: hold it to what the enemy's attack dealt (the forecast audit's own window of that turn, the projection made a turn earlier).
- [ ] A stun the projection sees coming (a certain hit waking a sleeper early, Shriek's threshold) ends that enemy's forecast there: its stunned turn isn't one of its rules' moves, so the forecast can't draw it. Draw the game's stun intent for that turn and go on from the stun's named move.
- [ ] The other turn's-end listeners not followed at the enemies' turn end (each makes the state after it unknown: turn-end-gaps.json; in a projection only Battleworn Dummy's time limit and Shrink still do, decision #308): BattlewornDummyTimeLimitPower.AfterSideTurnEnd (its encounter's RanOutOfTime), ChainsOfBindingPower.BeforeSideTurnEnd (its Bound cards cleared: a loop over the cards); and the powers whose turn's-end hook reads but whose other hooks don't (Borrowed Time, Encore, No Draw, No Energy Gain, Retain Hand, Ringing, Show Off, Shrink, Tangled), only on an enemy (a player's waits on taking part).
- [ ] Doom's kill's unknowns (decision #310): a pet a Doom kill takes isn't followed (`SimEngine.KillEach`: unknown), and Book Repair Knife's count of the Fatal deaths is unknown for an enemy with a power whose ShouldOwnerDeathTriggerFatal isn't a constant (ReattachPower's AreAllOtherSegmentsDead: `SimEnemy.DeathSetsOffFatal` null).
- [ ] A read model's unread step at your next turn's start makes a projection unknown even where all it changes is what a projection doesn't follow (found with decision #323): Crossbow, Seal of Gold, Energy Next Turn, Radiance, Star Next Turn and the powers whose BeforeHandDraw makes cards into your hand (Infinite Blades, Hello World, Creative AI...) are read whole with an `UnknownStep` there, while a class with no model doing the same is left out of a projection (`ForecastIgnores`, decision #308). Ask `ForecastIgnores` of an unread step's body too.
- [ ] The enemies' turn start's unmodelled listeners (found with decision #303, not marked): a class with no model whose turn's-start hook acts as the enemies' turn starts isn't followed there and marks nothing (Sandpit's was the one known to act there: modelled, decision #321). The same list and mark as the turn's end, but the enemies' turn start is before the enemy turn, so its unknown would be shown (`?`): the owner's call.
- [ ] A cost to look at (decision #302): a read model whose turn's-end steps wait on a field of its own (Orichalcum's ShouldTrigger, set only for its owner taking part) makes the enemies' turn end a step in every fight it's in (SimModel.MayActAtTheEnemiesTurnEnd can't tell).

---

## 8. UI coexistence rules (checked for every feature)

Existing game UI found in `Nodes/Cards/NCard.cs` and `NHealthBar.cs`:

- Card: title banner, energy/star cost (with cost recoloring), type plaque, enchantment tab (`%Enchantment`), affliction overlay (`%OverlayContainer`), highlight glow (`NCardHighlight`: cyan playable, gold, red), rarity glows, lock icon.
- Health bar: block icon/outline, Poison and Doom lethal previews.
- Relic bar: the relics' own counters and active state.

Rules:
- [~] Never reuse the game's glow colors (cyan `0,.957,.988`, gold `1,.784,0`, red `.83,0,.33`) or cost colors.
- [~] Every on-card indicator is a tab along the card's top edge; nothing covers the cost, title, enchantment tab or affliction overlay.
- [~] One visual language: every on-card indicator is a tab along the top edge (0.12.0); one tab per source; the row shrinks to fit.
- [ ] Everything works on enlarged (hover) cards and in grid/pile views.
- [~] A global hotkey hides all overlays.

---

## 9. Proc Tags → `proc-indicators` (built in 0.19.0; full source list in FEATURES.md)

**Goal:** show, on each card in hand, every power or relic that playing it would set off. "Damage is damage, block is block": if a card's play gains block or deals damage through a proc, the card should say so, including procs one step removed (a card that *exhausts another card* while Feel No Pain is up).

Two kinds of proc:
- **Trigger only:** "this card sets off that relic," like the existing relic reminders. Tab with the relic's or power's icon, no number. Examples: Intimidating Helmet, Mini Regent.
- **Quantified:** the proc gains block, deals damage or grants a stat, and the exact amount is worth knowing. Example: Feel No Pain on True Grit shows +3 block (+4 upgraded, +6 with two copies of the power).

### Decided (2026-11-09): source icon + number

Quantified procs use **option A**: one tab per source, with the source's own icon (power or relic) and the amount as a number (e.g. Feel No Pain icon, `3`). Trigger-only procs keep the plain icon, with no number. CLAUDE.md's UI rules and docs/FEATURES.md "On-card tabs" allow a number on quantified proc tabs only. Revisit option B if cards end up with too many tabs.

### Configuration: every indicator can be turned off (defaults: everything on)

Players should be able to trim what shows, down to single indicators. Defaults show everything.

- [~] **Per-tab toggles** (0.19.0): `Tabs` in settings, one key per tab kind (today: `retain`, `sly`, `replay`, `shuffle`, `double-play:<Source>`, `proc:<Source>`; `exhaust`, `relic:` and `blocked` are gone), added as each kind first appears, default on (apart from the every-card powers' tabs, decision #153). Replaced `ShowDoublePlay` and `DimBlockedCards` (migrated); `ShowInnateEternalBadges` removed with the tabs.
- [~] **Per-proc options** (0.19.0): `ProcNumbers` (show amounts), `ProcIndirect` (count procs one step removed).
- [~] **Group switches** (0.19.0): `relic:*`, `proc:*`, `double-play:*` in `Tabs`.

### Options considered

Before the decision, one tab per effect had the source's icon and **no numbers**. The options for quantified procs were:

| Option | Looks like | For | Against |
| --- | --- | --- | --- |
| **A. Source icon + number** | Feel No Pain icon, `3` | Keeps one tab per source, so you can see *why*; the number gives *how much*. Fits the current language with one change: numbers are allowed on quantified procs. | Three block procs on one card make three tabs. |
| **B. Block / damage icon + number** | Block icon, `3` (sum of all block procs) | "Block is block": one tab per outcome, totals summed. Fewer tabs. | Loses the source; needs block/damage icons from the game's art (intent or status icons). |
| C. Source icon only | Feel No Pain icon | No rule change. | Doesn't answer "how much". |

### Sources

| Done | Source | Cards marked | Effect, from the code (v0.111.0) |
| --- | --- | --- | --- |
| [~] | **Intimidating Helmet** (relic) | cards that will **spend ≥2 energy** | `BeforeCardPlayed`: if the energy actually spent (`cardPlay.Resources.EnergyValue`) is ≥ 2 (`EnergyVar`), gain 4 block (Unpowered). So it's energy spent, not printed cost: cost reductions and free plays don't count; X-cost cards count with ≥2 energy available. Trigger tab (or +4 block under A/B). |
| [~] | **Mini Regent** (relic) | cards that spend **stars**, while unused this turn | `AfterStarsSpent`: the first star spend each turn gives +1 Strength. Read through its private `UsedThisTurn` (reset at your turn start). Any star spend counts. Trigger tab. Also listed in §3. |
| [~] | **Feel No Pain** (power) | cards that exhaust when played (Exhaust, added Exhaust) **and cards that exhaust other cards** | `AfterCardExhausted`: `Amount` block per card exhausted (Unpowered). Exhaust-others cards: Brand, Burning Pact, Cinder, Cleanse, Fiend Fire, Flak Cannon, Purity, Scavenge, Second Wind, Stoke, Thrash, True Grit. Count exhausts per card (True Grit: 1 if there's another card in hand; Second Wind / Fiend Fire: per matching card; Burning Pact: its count). Ethereal end-of-turn exhausts go in Incoming Damage (§4). Stacks: two copies show double. |
| [~] | **Spirit of Ash** (power) | **Ethereal** cards | `BeforeCardPlayed`: playing an Ethereal card gains `Amount` block (Unpowered). |
| [~] | **Sleight of Flesh** (power) | cards that **apply debuffs to enemies** | `AfterPowerAmountChanged`: each non-temporary debuff you apply to an enemy deals `Amount` damage (Unpowered) to that enemy. Count applications per target; **Artifact** cancels an application (so it doesn't trigger), so subtract the target's Artifact charges. Multi-target cards count per enemy. |
| [~] | Dark Embrace (power) | exhausting cards (same list as Feel No Pain) | `AfterCardExhausted`: draws `Amount` per exhaust; Ethereal exhausts are saved up and drawn later. Trigger tab. |
| [~] | Charon's Ashes, Forgotten Soul, Joss Paper (relics) | exhausting cards | `AfterCardExhausted`: 3 damage to all enemies; 1 damage to a random enemy; draw at the 5th exhaust. Burning Sticks is already shown (§3). |
| [~] | Child of the Stars (power), Galactic Dust (relic) | star-spending cards | `AfterStarsSpent`: `Amount` block per star spent; 10 block per 10 stars spent (saved across combats). |
| [~] | Shroud, Vicious, Vital Spark (powers) | Doom / Vulnerable appliers; Tainted cards | Built in 0.19.0. Sandpit and Sword Sage don't react to card plays (checked). |
| [x] | **Full sweep** (2026-11-09) | | Every power and relic overriding the play, exhaust, discard, draw, stars, energy, debuff, block, damage, card-created and shuffle hooks. The resulting source list is in FEATURES.md "Proc Indicators". |
| [~] | Procs of procs, Metronome, Bone Flute, Rupture, Black Hole on star gain | | 0.20.0. Juggernaut counts block from every proc; Metronome counts Storm's lightning; Black Hole counts The Sealed Throne's stars. |
| — | On-draw procs (Pagestorm, Iteration, Automation...) | | Not shown by design: they fire on draw, not on play (decided 2026-11-09). |
| [x] | Chains | | 1.0.0: procs are simulator models, so chains follow from the hooks (a draw from Dark Embrace sets off Speedster, The Sealed Throne's stars set off Black Hole, Kunai's Dexterity counts for later block). Not yet: Hammer Time (multiplayer). |
| [~] | **Audits** | | 0.20.0: forecast and proc audits log mismatches to godot.log. Read the log before each round of work. |

---

## 10. Review pass (2026-11-09) → 0.21.0

Against the three pillars: (1) incoming damage covers everything ending the turn does, verified by the forecast audit; (2) the relic tray shows counters you push by playing; (3) a tag for everything a card play sets off that the card doesn't say, verified by the proc audit, including "unexplained" effects.

- [~] Forecast: end-of-turn damage to enemies (kills before they attack); HP-loss effects (Tungsten Rod, Beating Remnant, Buffer).
- [~] Forecast audit: end-of-turn block check; entries split by side; combat summaries.
- [~] Tags: enemy reactions to your attacks, kills, block breaks, HP loss from cards; Sovereign Blade buffs; Pael's Eye; replay `2×` text tab.
- [~] Relic Reminders merged into Proc Indicators.
- [~] Proc audit: budget check (unexplained effects), exact damage ranges, combat summaries.
- [~] Tray: Pael's Tears / Eye rows removed; turn timers opt-in.

---

## 11. Automatic bug reports (2026-11-09) → 0.22.0

- [~] Mod: Sentry reporting of crashes and audit mismatches with a full state snapshot (`ReportErrors`, on).
- [~] Workflow: Sentry → GitHub issues (`auto-report`), resolve on close, reopen on regression.

---

## 12. Settings menu (2026-11-09) → 0.23.0

- [ ] Check in combat: pause-menu button, toggling a feature / tab mid-turn, "only what's in this fight".
- Standing rule (CLAUDE.md): keep `Menu/SettingsCatalog.cs` in sync with new proc rules and tracker specs (the log warns on unknown tab keys and missing tracker rows).
- [~] 0.23.5: tab icons centered in the visible part of the tab (they sat 4% of the tab length too high).
- [~] 0.23.7: tabs stay on a held (dragged or aimed) card; tab icon nudged a little lower.
- [~] 0.23.8: Relic Counters beside Osty's HP bar at the default placement (`MovableWidget.AttachToWhenDefault`); bigger `2×` text. Check in a Necrobinder fight, including after Osty dies.
- [~] 0.23.9: proc audit uses each card's last tabs in hand and splits plays at the next card's energy/star spend (best fit for 0-cost); reports carry the before-play state, timing and window. Watch reports #11, #13, #17–#20 (VitalSpark/Tainted on Enfeebling Touch still to diagnose with the new context).
- [~] 0.23.10: flash list cleared per fight (0.23.9 regression: every "no tab" report #22–#26, #28, #30–#32, #17); 0-cost first play picks its start by best fit (Snecko Eye: #13, #21, #27, #29); potions end a window (#11, Lucky Tonic); fingerprints split by power / direction. Earlier fixes re-checked against this run's log and kept: own powers from code (no Doom flags), other cards' block excluded (no "predicted 0, actual 5"), last tabs in hand (tabs captured on delayed plays). Removed the old frame heuristic.
- [~] 0.23.11: orb evoke damage in kill tabs and the proc audit's damage range (`PlayFacts.EvokedOrbs` / `OrbDamageFor`). Found in game: every card would kill a low-HP enemy through a Glass evoke, only the attack had the tab.
- [~] 0.23.12: Imbalanced tab (block threshold for a full block, from the Incoming Damage simulation). Suggested in game.
- [~] 0.23.13: wake tabs judged by end of turn (play hits + orb evokes + proc damage per enemy from the first pass + `TurnEndModel.EndOfTurnHitsOn`). Found in game (Slumbering Beetle, Kusarigama + Lightning).
- [~] 0.23.14: Unsettling Lamp skips enemies whose Artifact covers the play's debuffs (UnsettlingLamp.BeforePowerAmountChanged checks HasPower<ArtifactPower>). Found in game.
- [~] 0.23.15: Feral tab (0-energy Attacks, charges left this turn). Suggested in game.
- [~] 0.23.16 (Defect run, reports #33–#72, all on 0.23.10): end-turn window close (PlayerEndedTurn), Frost evoke block, damage caps not procs, enemy→enemy powers ignored, Glass passive in end-of-turn hits, forecast audit tolerance for left-out random damage, Shriek with orb damage. Orb evoke damage (#34, #42, #45–#47, #49, #54, #56, #58–#61, #72) was fixed in 0.23.11, Feral (#62, #70) in 0.23.15. Open: Creative AI's generated card in its window (#67).
- [~] 0.23.17: RNG-aware audits (`PlayFacts.RandomOutcome`, `Hit.Random`; forecast Osty tolerance); Frost evoke block in Juggernaut and Imbalanced. No card tab for evoke block (by request: the card text/orb already says it).
- [~] 0.23.18: joint split for 0-cost plays (`ProcAudit.ResolvePending`; the greedy split pushed unmatched effects onto the next card: #69, #70, #76), Intangible as a damage cap (#73), proc damage skips enemies the card kills (#74, #75: Test Subject revive). Note: the game's CardDrawnEntry text says "discarded" (its own typo); those lines are draws.
- [~] 0.23.20: Juggernaut from end-of-turn block gains in the end-of-turn model (random target: counted with one enemy).
- [~] 0.23.21: potion effects out of play windows (#67: Power Potion's free Creative AI counted as a created card). `PotionModel` writes `PotionUsedEntry` after `OnUse`, so the 0.23.10 cut at the entry left the potion's effects in the previous window; the gap before the next play now stops at the entry and the previous window's end is a best fit before it (`ProcAudit.PotionCut`).

---

## 13. Outcome simulator (2026-11-09) → 0.24.0

- [ ] Remaining approximations: poison TriggerCount, a card's procs timing relative to its hits, block remainder across gains.
- When a report is resolved, note here which layer it was: simulator rule / adapter / audit comparison.
  - [x] Dev review 2026-11-26, the Waterfall Giant and Memento Mori: the lethal check's "as good as dead" rule (audit, decision #336); the revived Giant's poison preview kept (simulator: the preview field only the state check reads); Explode projected at 0 (adapter and simulator: the projection read a damage its About to Blow sets as it stood, decision #337).
  - [x] Dev review 2026-11-26, five projected-move mismatches (Fuzzy Wurm Crawler, Phrog Parasite, Wrigglers): audit timing; the projection was checked on a frame before the game rolled your new turn's moves (its BeforeSideTurnStart still running), so it was held to the moves just made.
  - [x] Dev review 2026-11-26, Play Preview with Thorns (Foolish Shot, Strike into a Toadpole): audit comparison; the preview counts the Thorns the play takes, the board after it doesn't (the replay reproduces 14 and 12: `PlayPreviewTests`).

---

## 14. Coverage (2026-11-10) → 0.30.0

- [ ] Verify in game (v0.114.0): Esc closes the settings menu and the inspect screen; Tune's tag on the turn's first Ki; Hard to Kill on an enemy with block.
- [ ] Mid-play card costs, one rule rather than a fix per class. Done: the cost passes read as Try hooks (decision #332); the live cost rule (decision #334): the costed card's pile (`switch (card.Pile?.Type)`, `HookFact.CardPile`), each hand card's cost before global modifiers and its facts (`SimHandCard.LocalCost`, `Card`), `SimEngine.HandCost` through every listener's pass then every Late pass, Corruption's hand-written Late pass, Simpleton's cheapest and Mummified Hand's pick asking it, `SimState.CostsUnknown` for a pass the simulator doesn't run (`?`, never silent), and `HandCostAudit` holding it to the game's cost on each board; Borrowed Time, Curious, Ensemble and Spiked Gauntlets read whole. Left: (a) Free Attack's, Free Skill's, Free Power's and Veilpiercer's `BeforeCardPlayed` (`cardPlay.Card.Pile`, the played card's pile): reading it makes them read whole, so their charge runs in the simulator and their tabs (the `free:` tags, `Procs/FreeCards.cs`) need a parity proof first; (b) Tangled (AfterApplied's loop), Void Form (AfterCardPlayed's `cardPlay`), Brilliant Scarf (`cardPlay.IsAutoPlay`): their other hooks; (c) cards as hook models (Cho Limbs' HasWahOrKiMelody, Coda's `MelodyCmd.MelodiesThisTurn`: CardListener models read from code, their history queries the card programs read); (d) a drawn card's cost of 0 (`SimDrawCard`'s, Drawn card readers: read as the game showed it before the play) and the energy a free card saves (`FreeCards.CostWithoutFree`: the game's own passes at the decision, right there, but not the simulator's rule) onto `HandCost`; (e) star costs (`TryModifyStarCost` reads but no simulation reads a star cost; Mummified Hand's `CostsEnergyOrStars` counts a card that costs only stars); (f) Corruption reads whole now (hook-gaps.txt): a parity proof to retire its hand-written model.
- [ ] Fur Coat on a creature joining mid-fight: unknown today (decision #325). To model it, read its marked-room test (its own saved coordinates against `RunState.CurrentMapPoint`: a fight constant the adapter could decide on the board) and translate `CreatureCmd.SetCurrentHp` of the creature added.
- [ ] Dampen's upgrades given back mid-play: read each downgraded card's upgraded copy on the board (as decision #218 does for the card in play) and swap them in as Dampen goes, so its last caster's death is followed instead of unknown.
- [ ] A creature killed during its own attack keeps its powers until the attack ends (v0.114.0: `Creature.IsAttacking`; an enemy's AfterAttack powers, a player's hooks): not yet checked against what the simulator keeps for a creature that dies mid-attack (Thorns on its attacker).
- Known gaps that could **underestimate damage to you**: none known. ~~Multiplayer Covered/Intercept~~ (0.37.4); ~~Queen and Kin Priest switching moves mid-turn~~ (reviewed for v0.113.1: neither switch changes an attack).
<!-- gaps:start (written by scripts/coverage-ledger.mjs from the review files; don't edit by hand) -->
- Known gaps (8; each is logged when present in a fight):
  - [ ] ChoLimbs (ledger): TryModifyEnergyCostInCombat takes 2 off its own cost once a Wah or Ki melody is played this turn: cards aren't read as hook models, so a board with it in a pile has every hand card's live cost unknown (SimState.CostsUnknown, decision #334): Simpleton's and Mummified Hand's picks there are unknown
  - [ ] Coda (ledger): TryModifyEnergyCostInCombat takes 2 off its own cost for each melody played this turn: cards aren't read as hook models, so a board with it in a pile has every hand card's live cost unknown (SimState.CostsUnknown, decision #334): Simpleton's and Mummified Hand's picks there are unknown
  - [ ] EyeWithTeeth (ledger): AfterAddedToRoom PowerCmd.Apply Illusion to itself as Fogmog's move summons it, which the enemy turn doesn't follow yet (TRACKER: summons in a move); on the board from the next decision
  - [ ] FurCoat (ledger): AfterCreatureAddedToCombat sets an enemy's HP to 1 as it enters a marked room's fight (CreatureCmd.SetCurrentHp of the creature added, its own marked-room test): the enemies there at the start are read live; one added mid-fight (a death's spawn: SimEngine.Spawn, which runs the hook's read listeners, decision #325) is unknown with Fur Coat on the board (SimSpawn.JoinUnread), its body not read (TRACKER)
  - [ ] GasBomb (ledger): AfterAddedToRoom PowerCmd.Apply Minion to itself as Living Fog's move summons it, which the enemy turn doesn't follow yet (TRACKER: summons in a move); on the board from the next decision
  - [ ] Parafright (ledger): AfterAddedToRoom PowerCmd.Apply Illusion to itself as The Obscura's move summons it, which the enemy turn doesn't follow yet (TRACKER: summons in a move); on the board from the next decision
  - [ ] ToughEgg (ledger): AfterAddedToRoom PowerCmd.Apply Hatch to itself (1 or 2 by whose turn it is) as Ovicopter's move summons it, or hatches it (CreatureCmd.SetMaxAndCurrentHp of a roll), which the enemy turn doesn't follow yet (TRACKER: summons in a move); on the board from the next decision
  - [ ] Zapbot (ledger): AfterAddedToRoom PowerCmd.Apply 2 High Voltage to itself as Fabricator's move summons it, which the enemy turn doesn't follow yet (TRACKER: summons in a move); on the board from the next decision
<!-- gaps:end -->

---

## 15. Out-of-combat reminders (Reward Tags, Event Reminders, Shop Reminders, Map Reminders: `reward-tags`, `event-reminders`, `shop-reminders`, `map-reminders`; Run Reminders until decision #185; goal 3 in ROADMAP)

An indicator on a choice outside combat when one of your relics changes what it does. Same language as on-card tabs (the relic's icon, crossed when it's against you).

- [ ] Play test: event icon placement (left of the option) on every event layout, ancients included. ~~The card reward panel's default spot.~~ (The panel is gone: decision #82.)
- [ ] Card removal (shop, events): no relic reacts today (`BeforeCardRemoved` has no relic); recheck each game update.
- [x] **Event options read as programs** (the read-from-code goal, decisions #195, #350): each option's code is read by ProgramReader into steps with their conditions and amounts (`Features/OptionPrograms.cs`), through the event's own methods, its base class's, the rewards it makes (their `OnSelect`), the event's own parts, delegates the event or the option holds, and a relic it gives through its own `AfterObtained`; the icons and the event check use the one reading, and the old flag-level scan is a check that the reading misses nothing it finds. Hefty Tablet's curse is read through its `AfterObtained` (`CreateCard<Injury>`). Coverage on beta (v0.114.0, `Snapshots/event-options.txt`): 270 options, 230 read whole, 2 whole once shown, 38 with a gap (14 a relic its code picks as you choose, 10 a reward's relic, 9 a card reward's alternatives, 6 a relic the event holds, 1 a delegate given to a card choice); 86 relics' `AfterObtained`, 76 whole. Main (`event-options-main.txt`): 252 options, 215 whole, 2 once shown, 35 with a gap; 84 relics, 74 whole.
- [x] Ancient options: every ancient's relic option is read through the relic's `AfterObtained` (`Snapshots/event-options.txt`, the `RelicOption.OnChosen [Relic]` lines, and every relic's `relic X:` line): cards, curses, gold, potions, max HP, HP, upgrades, enchantments, transforms, removals and relics they give. Their gaps are the same reasons as the events' (a reward's relic: Neow's Bones, Calling Bell, Toy Box, Small Capsule; a card reward's alternatives: Glass Eye, Kaleidoscope, Lost Coffer, Orrery; Large Capsule's relics; Claws' preview).
- [ ] Event options, a relic the event holds (Museum of Torture, Relic Trader, Thermae Nonupeipe's waters, Wongo's featured item; 6 options): read the relic the option shows (its hover tip) through its `AfterObtained` as the option is shown, as a held delegate is; today it's a named gap and the check reads the relic after the fact.
- [ ] Event options, a card reward's alternatives (`CardRewardAlternative.OnSelect`, 9 options and 4 relics): skip, a relic's sacrifice. Read the alternatives a reward offers (`TryModifyCardRewardAlternatives`) from their code.
- [ ] Event options, a relic picked as you choose or a reward's relic (14 + 10 options, 5 relics): never read before you choose (the pick is the RNG's); the check reads the relic you got. Owner's call: an icon for "any relic from this pool could do X" (the pool is knowable), or leave it to the check.
- [ ] Self-Help Book's options read as enchanting your cards (the program), but the card walk misses the generic `SelectAndEnchant<T>` (its state machine is generic), so Mystic Lighter's enchantment check (`ChangeReaches`) says no: read which enchantments an option names from the program's steps instead of the walk.
- [ ] The cards an option can give (the card walk, decision #58: pools, filters, odds) are read apart from the program: read them from the program's step values (`CardCreationOptions`, `CreateForReward`), so a relic the option gives (Fragrant Mushroom's upgrades, Claws' Mauls) counts for the card relics too.
- [ ] Test in game (co-op): a teammate's tab on your card (Rally with a teammate's Juggernaut), and no false tab on yours for their firing.
- [ ] Test in game: the card reward tabs (Bing Bong, Lucky Fysh, Book of Five Rings on the healing pick) beside Strike Dummy's.
- [ ] Test in game (1.15.1): shop icons clear of the coin with their backdrops, Lucky Fysh's net price centered under the price; map legend icons level with the legend's own.
- [ ] Test in game (1.15.2): the Inspect button right of Colorful Philosophers' options opens the Inspect screen.
- [ ] Test in game (1.16.0): the heart marker's placement on the Book of Five Rings tab, plain and crossed.
- [ ] Test in game: About page (links open the browser, paging, search, long notes wrap); ~~card reward panel above the cards (clear of the banner and the offered-card tabs, 3 and 4 cards with Lasting Candy)~~ (gone: decision #82); shop price icons clear of the coin (Book of Five Rings on a card); map legend icons (placement left of the legend, hover tooltip and room highlight, hidden with the legend), Dowsing and Byrdonis Egg as tiny cards, the regrouped settings pages.
- [ ] Event options whose effect runs through a delegate the reader doesn't follow (a lambda passed to a card choice): the check reports a relic that reacts without its icon. Done (decision #350): a delegate the event or the option holds (the Conveyor's dish, a Neow modifier's option), one given to a query that runs it (Neow's Bones' relic rewards), a reward's `OnSelect`. Left: a delegate given to a card choice (Claws' transform preview, `CardSelectCmd.FromDeckForTransformation`: "commands in a callback"; its Select lambda reads the same steps, so nothing is missed today).
- [ ] Test in game (decision #350): Room Full of Cheese's Gorge and Brain Leech's Share Knowledge show Bing Bong, Book of Five Rings, Lucky Fysh and the card relics; Hungry for Mushrooms' Fragrant Mushroom shows Tungsten Rod; the Lantern Key's Fight shows the card relics; Trial's Accept and the Waterfall's Enter show none; the `[audit] [reminders]` lines carry each option's reading.

---

## 16. Open concerns (2026-11-10, waiting on play testing)

- [ ] **A black screen at a run's start** (2026-11-25, dev build be2bda4, Regent A10, seed 250M3CQPT2AV): the main menu's Common preload never finished, and the run's preload waited on it. In that launch Quick setup had opened during the preload and loaded card frames, banners and icons; in the four other launches that day the same overlap happened and the preload finished. Fixed as the likely cause: the settings menu waits for the game's preload (`Cards.GamePreload`). Two things new that day may have made the race likelier: dev mode's performance trace attached to every launch (dotnet-trace installed that morning) and the heavier work at load since 2026-11-24. Watch: a `[preload]` warning or a `preload` report (a preload unfinished after 30 s, with what it was loading) means it's back, with evidence. If one comes, check whether the trace was on and what else the mod was loading. Close once a week of launches passes with none.

- [ ] **A card Feral sends back makes Stampede's pick unknown** (2026-11-21): `SimEngine.AddToHand` marks the hand's contents unknown for any card joining it, but the card Feral returns is known (the one just played). Keep it in `AutoHand` instead.

Recorded before the first runs with 0.26–0.31; revisit with real reports.

- [ ] **Review accuracy:** ~400 reviewed entries (coverage ledger, relic tracking) were written in one session from the hook bodies. Some "covered" notes rest on understanding of the mod's rules rather than a test; coverage reports and the snapshot check should expose any that are wrong.

---

## 23. Potions (decision #313, 2026-11-25)

The owner's decision: drinking or throwing a potion is a decision the mod previews. Each potion's use is read from its code (`SimAdapter.Potion`); `Snapshots/potion-uses.txt` lists what every potion is built into, and the potion scan (`PotionScanTests`) holds every potion's commands on both branches to a translation or a named gap. Each `?` below is a potion's use the simulator doesn't follow yet.

- [~] Play Preview for potions (decision #314): built, verify in game: pointing at a Drink (Block Potion, Weak Potion's Throw then aiming at an enemy, Explosive Ampoule, Swift Potion), the lavender Incoming Damage, the Death Skull's odds and the intents' lines after it; nothing for Entropic Brew; in multiplayer, throwing a Block Potion at a teammate (their Incoming Damage), and Count teammates' hands off/on. Check the dev dashboard's searches for the potion's (`<Potion> (potion at <aim>)`) and that pointing at a Drink allocates nothing on a steady frame.
- [~] One translator (decision #335): built, verify in game with Play Preview and the potion check's `[audit] [potion]` lines: Swift Potion's draws and Attack Potion's card (your hand one larger), Bottled Potential (your hand into your draw pile, shuffled, then 5 drawn), Glowwater (your hand exhausted, then 10 drawn), Gambler's Brew (the most it can discard, as many drawn), Droplet of Precognition with cards in your draw pile, Fruit Juice's max HP; in multiplayer, Blood Potion and Duplicator thrown at a teammate.
- [ ] Cards you pick, how many your choice (Ashwater's exhausts, Gambler's Brew's discard and draw its count): translated as the card translation takes Purity's (0 to 3): the most it can take, no `?` (decision #335). Decision #188 says a choice of how many keeps its `?` (Neow's Fury's and Guards' do): the owner's call whether Purity, Ashwater and Gambler's Brew show `?` too (a `?` more on screen), or a range.
- [ ] Bottled Song's melody cards (decision #339): its list's fills read (an `Add` in a loop over its three melody tags, each on the factory's card being there), but the loop's bound is the length of an array the compiler fills from a data blob (`newarr; dup; ldtoken; RuntimeHelpers.InitializeArray`, kept in a closure's field), which the reader doesn't read, so the count is unknown where it runs. Read `newarr`'s length through `InitializeArray` (a void call on the dup'd array), so `ldlen` of it is its count.
- [x] Your hand upgraded (Blessing of the Forge: `CardCmd.Upgrade` of each upgradable card in your hand): followed as a card's upgrade of your hand is (decision #347: `UpgradeCardsAction`).
- [ ] Thrown at a teammate: their orb slots (Potion of Capacity: `OrbAction.Slots` has no player), and what acts on their cards (their hand exhausted, discarded, upgraded, moved or shuffled, cards made or forged for them, played from their draw pile: `SimAdapter.ActsOnCards`): a teammate's piles aren't simulated, so each is `?`.
- [ ] A card's damage to every creature (`CreatureCmd.Damage` of `CombatState.Creatures` but the pets from a card, with its card as the source: no card does it today) is unknown where it runs; a potion's is followed (decision #340). Also unknown: such damage with an enemy on the board that can't be hit (it's in the game's list; the simulator's hits don't reach it).
- [ ] What a potion's code changes outside a command: Soldier's Stew (each Strike's `BaseReplayCount`: its OnUse isn't read, `?`), Touch of Insanity (a picked card's `SetToFreeThisCombat`: not read, and nothing ending the turn now reads it). Read as a power's or relic's CardActionStep is.
- [ ] Potion Tags for a relic that reacts to what a potion does (a power it applies, block or damage it deals), not to the potion hooks: the use's log already shows them (decision #318 keeps the tags to relics answering a potion's use, as decision #41 had them); showing them is the owner's call.
- [ ] Entropic Brew gets no preview (the owner's decision: its random potions don't change the fight); its use (a loop of `PotionCmd.TryToProcure` while you have room) changes nothing the simulator follows unless a listener answers `AfterPotionProcured` (Belt Buckle: then `?`), as a card's procure is (decision #335).

## 22. Model programs: powers and relics (decision #195, 2026-11-24)

- [ ] Hooks no read model runs yet (the biggest gap kind; round two added AfterDamageGiven, AfterDamageReceived, every turn-end hook of the players' side, ModifyCardPlayCount and AfterModifyingCardPlayCount; round twenty-seven AfterBlockBroken and ShouldClearBlock, decision #285): BeforeDamageReceived, AfterPreventingBlockClear (Blur, Sturdy Clamp: a player's block, kept past the turns a simulation covers), AfterAttack/BeforeAttack, AfterBlockGained, AfterOrbChanneled, AfterDeath, AfterApplied, ModifyCardPlayResultLocation, the HP-loss pipeline, ModifyHandDraw and the Try/Should hooks.
- [ ] A death's removal of its creature's powers (CreatureCmd.Kill: Creature.RemoveAllPowersAfterDeath, then each one's AfterRemoved) isn't told (decision #288): a dead enemy's powers stay in the simulator's state, unheard. No read power's AfterRemoved acts on a dead owner today (Burrowed's block loss of a dead creature does nothing; Intercept's and Vital Spark's are unknown, on a player and an enemy whose death the turn's outcome already shows). Tell it as the powers go once a class's AfterRemoved would act there.
- [ ] The power removals' order: an application that leaves a power at 0 tells its AfterRemoved as it goes, inside SimEngine.ApplyPower, where the game tells Hook.AfterPowerAmountChanged's listeners first (PowerCmd.ModifyAmount, then PowerCmd.Remove). No read power's AfterRemoved changes what such a listener reads today; tell it after them (through ApplyAndTell's and the card's applications' branches) once one does.
- [ ] The AfterRemoved left for the hand-written models, each with its conversion: Covered (Intercept's own data: InterceptPower.RemoveCoveredCreature, IsCovering, its owner's Covered copies by applier), Sword Sage (SovereignBlade.BaseReplayCount changed outside a command, beside its AfterCardEnteredCombat). Phantom Blades' only tells its Shivs their keywords changed (CardModel.InvokeKeywordsChanged: shown only), nothing a fight reads.
- [ ] The play's shape's rest: ModifyCardPlayResultLocation is read (decision #286: Rebound read from its code); Nostalgia is read too (decision #290: the fight's log asked for two card types in one question, a count of each summed); Corruption on TryModifyEnergyCostInCombatLate, Feral on its play's energy (resources.EnergyValue); Echo Form (the fight's log in ModifyCardPlayCount, a VFX node), Tag Team (instanced).
- [ ] A hit's reactions' rest: Monarch's Gaze (a power of its own on the target), Self-Forming Clay (an outcome tab), Concoct (its owner's side), Imbalanced, Sic 'Em, Mighty Wings, Rupture, Centennial Puzzle, Demon Tongue, Personal Hive, Flutter, Plow, Shriek, The Gambit (stuns, a kill, outcome tabs, the draw pile), Inferno, Hardened Shell, Slippery, Beating Remnant (the HP-loss pipeline).
- [ ] Cacophony (instanced copies).
- [ ] AfterDeath's rest, each by its first gap: Crab Rage, Gremlin Horn (the dead creature's side), Compost, Possess Speed and Strength (a loop over creatures), Ravenous, Infested, Stock (a monster's own state set), Surprise (a power's Target set), Surrounded (its own method), Impaled (DislodgeAllArrows), Shrink (creature.IsPlayer), Dampen (its own data by creature).
- [ ] The turn's end's rest: Stone Calendar (the turn number), Ripple Basin (the fight's log in a Before hook), Pael's Tears (a tab on the outcome). Insatiable Dominion is read from its code (decision #283: a nullable's GetValueOrDefault, a player's combat state there).
- [ ] The data group's rest, each by what else it needs: Dark Embrace (the Ethereal exhaust at the turn's end, `causedByEthereal`), Automation, Orbit, Monologue, Cacophony, Panache (instanced copies), Oblivion (Doom applied by its applier), Rupture (AfterDamageReceived), Yah (an arrow's owner), Pale Blue Dot (the plays-finished count), Imitation Learning, Pocketwatch, Conductor's Baton (their own fields used directly).
- [ ] The hand-written models left (Sim/Models: 33 hand-written models after Possess Strength and Possess Speed (decision #326: written by hand, the reader reads no dictionary of its own data keyed by creature nor a loop over it; 31 before them); 32 hand-written models after Consuming Shadow (decision #291); 33 after Nostalgia (decision #290); 34 after Plating (decision #289); 35 after Snecko Skull (decision #287); 36 after Rebound (decision #286); 37 after Hand Drill and Burrowed (decision #285; Barricade read with them, the engine's Barricade-or-Burrowed flag gone); 39 after Insatiable Dominion (decision #283); 40 after Shadowmeld (decision #280); 41 after Magic Bomb (decision #279); 42 after Hardened Shell (decision #278); 47 after Plow (decision #275); 48 after Curl Up (decision #274); 49 after Pen Nib and Vambrace (decision #272); 51 after Imbalanced (decision #271); 52 after Intercept (decision #270); 53 after Stretch and Pael's Legion (decision #269); 55 after Serpent Form, Calamity and Juggling (decision #268); 58 after round nineteen (Devour Life and Sic 'Em read from their code: OstyCmd.Summon a read step, decision #262); 63 classes after round seventeen (Black Hole, Shriek, Flutter, Ravenous, Burning Sticks, Song Path, Violent Chord read from their code, decisions #250 to #253; The Gambit kept for a player's Gambit kill, a display decision; the scan's floors beta 247 power and 80 relic bodies read, main 209 and 73); 70 classes after the bonus tabs by row (Conqueror read from its code, its tab where its term changes the play: decision #247; 71 before it, after round sixteen); 76 classes after round fifteen (the bonus tabs read from the code, decision #239: Art of War, Ripple Basin, Parrying Shield, Self-Forming Clay, Emotion Chip, Demon Tongue, Slippery, Monarch's Gaze; Conqueror kept for its tab, the owner's call; the scan's floors unchanged); 84 classes after round fourteen (Twine and Yah read from their code, decision #236; the enemy turn's hits back and attack hooks, decision #233; every power class's PowerCmd rules, decision #234; the Play audit's name lists, decision #235; Slippery now reads whole and keeps its model for its outcome tab; the scan's floors beta 232 power and 79 relic bodies read, main 199 and 72; the method list 20 engine rules naming 135 game methods, 3 adapter rules); 86 classes after round thirteen (2,281 lines counted whole: No Block, Vigor, Gigantification, Bone Flute, Hard to Kill, Intangible, Slow, Buffer, Lethality and Aim read from their code, decisions #227 to #232; Dislike You, Honing and Calcify newly read by the rule; the scan's floors beta 231 power and 78 relic bodies read, main 199 and 72; the method list 20 engine rules naming 132 game methods, 3 adapter rules); 96 classes after round twelve (2,418 lines counted whole: Vital Spark, Mummified Hand and Music Box read from their code, decisions #223, #226; the scan's floors beta 208 power and 77 relic bodies read, main 184 and 71; the method list 20 engine rules naming 132 game methods, 3 adapter rules); 99 classes after round ten (2,455 lines counted whole: Razor Tooth, Master Planner, Explosive and Galvanic read from their code, decisions #218, #220, #221); 103 classes after round nine (2,440 lines counted whole); 106 classes after round eight (2,475 lines counted whole); 110 classes after round seven (2,510 lines counted whole, 2,577 before it); 116 classes, 2,296 lines after round six; 119 classes, 2,325 lines after round five; 127 classes, 2,848 lines after round four, by capability in `Snapshots/hook-gaps.txt`; 143 classes, 3,046 lines after round three; 152 and 3,112 before it, 167 and 3,231 before round two), by what holds each, the first gap its code reads to (2026-11-24, after round two): the model's own internal data or fields used directly (Calamity, Monologue, Oblivion, Panache, Pale Blue Dot, Imitation Learning, Yah, Dark Embrace, Orbit, Automation, Curl Up, Stretch, Rupture, Pocketwatch, Conductor's Baton); the damage-time hooks no read model runs yet (AfterDamageReceived: Self-Forming Clay, Emotion Chip, Demon Tongue, Beating Remnant, Inferno, Personal Hive, Centennial Puzzle, Hardened Shell, Slippery, Asleep, Slumber, Plow, Shriek, The Gambit, Flutter; AfterDamageGiven: Monarch's Gaze, Imbalanced, Sic 'Em, Concoct, Mighty Wings; BeforeDamageReceived: Thorns; AfterBlockBroken: Hand Drill, Burrowed; AfterModifyingHpLost / AfterModifyingDamageAmount: Tungsten Rod, Buffer, The Boot, Slow, Hard to Kill, Intangible; AfterCurrentHpChanged: Red Skull, Necro Mastery); AfterDeath (Magic Bomb, Crab Rage, Ravenous, Infested, Steam Eruption, Surprise, Explosive, Stock, Compost, Gremlin Horn, Surrounded); AfterApplied (Plating, Insatiable Dominion, Serpent Form, Juggling, Feral, Echo Form, Lullaby, Impaled, Reaper Form, Phantom Blades, Covered); the end-of-turn Before hooks (The Bomb, Stone Calendar, Ripple Basin, Pael's Tears); the play's shape (AfterModifyingCardPlayCount: Tag Team; AfterModifyingCardPlayResultLocation: Corruption, Rebound, Nostalgia); AfterAttack (Bone Flute, Skittish, Vigor, Aim, Gigantification), AfterArrowShot/Impaled (Covering Fire, Quietus, Explosive Arrows), AfterCardEnteredCombat (Vital Spark, Smoggy, Sword Sage, Galvanic), AfterOrbChanneled (Metronome), AfterHandEmptied (Unceasing Top), ModifyXValue (Chemical X), AfterModifyingPowerAmountGiven (Snecko Skull); values and queries no fact reads yet (Pael's Legion, Vambrace, Shadowmeld, Parrying Shield, Violent Chord, Song Path, Motif, Child of the Stars, Iteration, Unsettling Lamp, Galactic Dust, Vulnerable, Weak (another model's method: Paper Krane's, Debilitate's), Pen Nib, Unmovable, No Block, Conqueror, Pagestorm, Joss Paper, Lethality, Serrated Arrows, Tracking, Colossus, Intercept, Twine); commands not translated (CardCmd.ApplyKeyword: Master Planner, Music Box; PlayerCmd.GainStars: Lunar Pastry, The Sealed Throne; CardCmd.Upgrade: Razor Tooth; AddGeneratedCardToCombat: Burning Sticks; AutoPlayFromDrawPile: Lost Lute; OstyCmd.Summon: Devour Life; a power's own method: Black Hole; AddToCombatAndPreview: Withering Presence; a made Soot: Biiig Hug), a loop (Consuming Shadow, Trash to Treasure), a pick (Stratagem), a damage to a list made in place (Haunt), Strength applied by someone else (Tender: its applier is the card's), a turn-start hook (Pael's Eye, Conductor's Baton's power, Art of War), a game object changed outside a command (Mummified Hand); read whole but its hand-written model counts instanced copies on their own (Cacophony); models of the simulator's own with no game class by that name (Void's and Regret's card listeners, Seeking Edge and Parry on Sovereign Blade's plays, a teammate's Impaled).
- [ ] Enchantments left: Slither's AfterCardDrawn (the engine runs no read enchantment there yet: the adapter's rule stands), Glam's and Spiral's EnchantPlayCount (read live through the game), Imbued's AfterAutoPrePlayPhaseEntered (the next turn's start), Goopy's AfterCardPlayed (its Amount grown: read live). Momentum's OnPlay and Vigorous' AfterCardPlayed read whole but store only what their damage math reads, which the adapter calls live (`PlayGrowth`).
- [ ] An orb's unpowered damage isn't put through Hook.ModifyDamage's additive and multiplicative terms (the engine's orb damage, as before). Checked on beta v0.113.1 (round ten): every game ModifyDamageAdditive and ModifyDamageMultiplicative (44 classes) tests a powered attack or its card source, but Surrounded (its own owner as the target: a player, never an orb's target) and Hang (Hang's card): none changes an orb's damage on an enemy. The caps do (Hard to Kill, Intangible: any damage to their owner), and the orb's damage goes through SimEngine.Damage, which applies them. No prediction changes; a scan holding every new damage modifier to that (a catalog test) would keep it so.
- [ ] The card's facts' rest: Lethality (the source card's pile and play index, the Modify hook's cardPlay), Tag Team (the card's TargetType, ModifyCardPlayCount's target), Phantom Blades (the source card's tags), Pael's Eye (an auto-play).
- [ ] A copy's values that aren't whole (Guarded's DamageDecrease 0.5) are the class's: the simulator's fields keep whole numbers. None differs between copies today.
- [ ] A read model's merged locals as a sequence (a let step per local) instead of a tree: Vulnerable's pruned value is 42 KB and asks its locals again on each way.
- [ ] Commands' rest: CardCmd.ApplyKeyword and CardCmd.Upgrade of another card than the one played (Music Box's clone; the card played is read since decision #218: Master Planner, Razor Tooth), AddGeneratedCardToCombat and made cards (Burning Sticks, Biiig Hug, Personal Hive), AutoPlayFromDrawPile (Lost Lute), CreatureCmd.Stun and Kill (Shriek, The Gambit), a model's own method (Black Hole's DealDamageToAllEnemies, Surrounded's UpdateDirection, Impaled's DislodgeAllArrows).
- [ ] Outcome tabs' rest, from code: stuns (Shriek, Plow, The Gambit's kill, Flutter, Ravenous: a monster's own state set first; Hand Drill's block broken and Burrowed's stun read since decision #285), Self-Forming Clay, Parrying Shield, Monarch's Gaze, Ripple Basin and Art of War's "gives up its bonus" (Seen), Pael's Tears.
- [ ] The fight's log's rest: `FirstOrDefault(...)?.CardPlay == cardPlay` (MelodyCmd.IsFirstPlayThisTurn: Violent Chord, Song Path), a question about one card play (Unmovable: `e.CardPlay != cardPlay`), Ripple Basin's crossed tab and its going dark (Seen) from its code.
- [ ] Part B3's card hooks (decision #194): `CardEnergy.Rules` (Epic Retrieval, Drum of Battle), `ReturnToHand`, end-of-turn auto-plays (Howl from Beyond, I Am Invincible) and Kingly Kick's cost on draw, read as programs: not started this round (they're cards' own hooks: CardListener models in the order of IterateHookListeners).
- [ ] A hook run that branches (a random target) and then asks a test a later step needs again: each branch asks it again (`HookArgs.On` keeps what was asked before the branch). No read model does that today.
- [ ] Verify in game: the relic counters' fields read from the game mid-turn (Kunai at 2, then a third Attack: Dexterity), Rainbow Ring after an Attack and a Skill, Mini Regent once a turn.
- [ ] The adapter names 20 power and relic classes not modelled from code and 9 read from code (model-kinds.txt "name gates"): each a rule to read from code or a reviewed reason. Round twenty-six (decision #284): a card's power applications no longer gate Poison by name (a class read from its code at review is run, or unknown once unread); PoisonPower's one name left in SimAdapter.Steps is its command's translation key (PoisonPower.Trigger, Outbreak's). Round forty-three (decision #331): Artifact is read from its code, its PowerCmd.Remove<ArtifactPower> in Expose's StripBlockAction the one name left (a command's shape). Left: Temporary Thorns as Thorns (SimPowerName: TemporaryThornsPower's BeforeApplied, AfterPowerAmountChanged and AfterSideTurnEnd unread; the engine runs no read BeforeApplied), Fool.PowersTouched (the play audit's explanation; Impaled is hand-written), MoveBranches' Strength and Personal Hive, BranchRules' Plow.
- [ ] The poison preview (`SimEnemy.Poison`, PoisonTriggers) is still the engine's, for the play check's comparison with the game's CalculateTotalDamageNextTurn: its code is a loop summing `Hook.ModifyDamage` of each tick, which needs a loop's value (a sum over a counter loop) read as a value.
- [ ] Pets, the rest: a hand-written model still names only your Osty as your pet (Hand Drill's AfterBlockBroken breaker: `owner == Who.You && breaker == Who.YourOsty`), so a teammate's Osty's hits don't count for theirs (Pen Nib read from its code, decision #272: `dealer == base.Owner.Osty`); a pet's own read fields (SimEngine.FieldsOf) aren't kept (none of today's pet powers has one); a pet the simulator doesn't keep (any but Osty and the Wuppy relic's) is no listener.
- [ ] A card play inside another (decision #267): the read models' bindings are each card play's; the hand-written model that keeps a card play in its own Data (Unsettling Lamp) still keeps one per model, so a play inside the one it kept can take it over; it goes with its conversion (Vambrace and Pen Nib read from their code: a card kept is its card play's identity, decision #272). Stretch and Pael's Legion are read from their code since decision #269 (a card play kept is that card play's identity): Vambrace's TriggeringCard is a card kept from a block's cardSource, which the reader doesn't read yet.
- [ ] ProcAudit's name lists left (decision #266 read the per-source effects and the stuns the turn's end makes from code): `SimComparedPowers` (the after-play check's own fields: Poison, Plating, Juggernaut, Thorns, Flame Barrier, Feel No Pain, Flutter), `DrawReactive`, `KillReactive`, `HpLossReactive`, `TeammateOnly`, `LaterPlayOnly`, `Stats`: each a question its sources' code (or their read specs) could answer.
- [~] The adapter's `SelfPowers` name list (a card's power without a PowerVar): Feel No Pain, Thorns and Flame Barrier are out of it (read from their code, the card's step's amount read for every card that applies them: decision #281); Plating too (decision #289); Juggernaut and Accelerant stay until they're read from their code. The draw profile's reviews by name (`DrawReactions`) of classes read from their code are their readings' now (`DrawnCardNotKept`).
- [ ] Verify in game: The Gambit played, then an enemy attack through your block: the Death Skull, and the forecast audit's death.
- [ ] Imitation Learning (round twenty-one's own-data group, left hand-written): its AfterCardPlayed auto-plays a copy of the Power card its target played, for its owner (CardCmd.AutoPlay of `cardPlay.Card.CreateCloneForPlayer(base.Owner.Player)`, after PowerCmd.Decrement(this) when Hook.ShouldPlay allows it), and the simulator plays no teammate's card: read from its code, that copy would make every simulation it's in unknown (a new `?` on a teammate's prediction: the owner's call), where the hand-written model only shows its tab and leaves the copy out. The reader would also need its `_playerTarget` (the adapter reads it as the copy's aim now: decision #321) and its list of (card, clone) pairs (a play bound, `FindIndex` its take). The card's existing-instance path is followed (decision #322).
- [ ] The wake comparison (decision #307) where an enemy's move rules can't be read (SimEnemy.Rules null: a state kind the reader doesn't know, a hidden intent): the stun's own plain rule stands, a bonus where the play wakes a sleeper. Make it unknown there instead (a `?` on the tab), the owner's call on how it looks.
- [ ] The wake comparison's roll in a card's simulation is asked as the enemies' turn ends: the players' BeforeSideTurnStart, which runs before the game's PrepareForNextTurn, isn't run there (no listener known changes a sleeper's power then). The projection runs it (decision #306); a card's simulation could too, behind a check that some listener acts there.
- [ ] A creature a death adds during the enemy turn isn't added (decision #276): it joins with no move that turn, and an enemy move's targets are fixed as the turn is read (MovePowerAction.To), so nothing after it in the turn reads it; its HP would be a branch of the enemy turn, which runs on one state. Add it (and its branches) once anything in the enemy turn could read it.
- [ ] A creature a death adds has no spawns of its own read (the next Axebot's next, should it die in the turn it joined): its read Stock finds none listed, so its death adds none (as a state with no spawns listed). The adapter could read them from the set-up monster's starting powers (a power with its amount, made off the board).
- [ ] Adaptable (Test Subject, ledger "ignore"): its AfterDeath calls TestSubject.TriggerDeadState (SetMoveImmediate(DeadState, forceTransition: true): a read step now, SetMoveStep), keeps its dead Test Subject in the fight and stays on it; its gaps: the run's TestSubjectKills (RunState.ExtraFields), its own data's isReviving, a dead enemy kept whose move heals it (Respawn: CreatureCmd.Heal of its own dead creature). Read, it would follow the respawn and its ShouldAllowHitting.
- [ ] Imbalanced on Bowlbug Rock (decision #271): its stun is its Headbutt's on the IsOffBalance the power sets, so a move the adapter doesn't read whole (a hidden intent) leaves the store unread and no tab (the hand-written model tabbed the first hit blocked regardless).
## 21. Model programs: enemies (decision #195, 2026-11-24)

- [ ] Moves the enemy turn doesn't follow yet (named gaps, `MoveCommandsNotTranslated`): summons (`CreatureCmd.Add`: the summoned creature isn't in the enemy turn's state), heals, a monster's own death or escape after its attack, max HP set, cards into your piles (your next turn's), Queen's switch to Enrage. Steps on a condition and powers made first (`MoveStepGaps`, reviewed in `EnemyMoveTests`: none comes before a later hit and changes it).
- [ ] Surprise's hand-written model still pushes its spawns by ordinal (`PendingSpawn` with no add named, decision #345): read it from its code (its AfterDeath reads whole once its foreach over its owner's Thievery and the Heist applications to the made Fat Gremlin are read steps), so its adds are keyed like Infested's and Stock's.
- [ ] Spawns not read (`SpawnGaps`): Infested's four Wrigglers (added in a loop; a branch per HP each, 5×4×3×2 ways, and they start stunned), Stock's next Axebot (a monster set up before it joins: its stock and HP). The spawn still joins only through Surprise's model (`SurprisePower.AfterDeath` pushes it); a death that adds creatures through another power needs the engine to add them on any death (next round, with powers).
- [ ] Show the forecast's next intent from the simulated state (after your planned plays: a kill, HP below a breakpoint): the owner's call (indicator density); the machinery is there (`NextOn`).
- [ ] Flags the simulator could follow instead of deciding when read: Queen's HasAmalgamDied (her AfterDeath's store when the Amalgam dies), Test Subject's Respawns, Knowledge Demon's counter: the hook programs' stores (next: model state in the simulator).
- [ ] A move changed mid-turn by a hook (Queen's Enrage when the Amalgam dies) or by a stun read before the play: the enemy turn runs the move read with the board (`SimEnemy.Move`), not the one a hook sets during the simulated play. Today only Queen does it, and neither of her moves attacks.
- [ ] Summons in a move (`CreatureCmd.Add` in Fabricator, Fogmog, Ovicopter, The Obscura, Living Fog, Two-Tailed Rat): the summoned creature isn't in the enemy turn's state (it acts next turn; a random-target hit of yours at end of turn can't land on it then). A summoned monster's AfterAddedToRoom powers (Zapbot's High Voltage, Gas Bomb's Minion, Eye with Teeth's and Parafright's Illusion, Tough Egg's Hatch) are ledger gaps for the same reason (decision #330); `SimAdapter.StartingPowers` already reads them for a death's spawn.
- [ ] Test in game: a fight where an enemy's Vulnerable lands before another enemy's attack (Queen and the Amalgam; Mawler with company), and The Obscura's Wail with Parafrights: the forecast audit's HP lost.

## 20. Replays (decision #160, 2026-11-21)

- [ ] Thrash's replay (its damage grows by the exhausted card's): not modelled, `?`.
- [ ] Memento Mori's attack hits a random hittable enemy in the predictions (`SimAdapter.ModeOf`: Targeting on a card aimed at a random enemy is read as a roll), but its code picks the one with the least HP (MinBy(HittableEnemies, c => c.CurrentHp), a tie the first of them). A target the step's own code picks from the board as it runs (the weakest: as HookTarget.WeakestEnemy is for hooks) needs an attack mode of its own.
- [ ] Draw profiles split by a condition's filter while a card that asks it is in your hand (Impatience's Attacks, decision #190), as by a take's (decision #189): more branches on draws then, a single play included. Worth splitting only when a later play asks it.
- [ ] Replay: a teammate's draws (Huddle Up) and their piles aren't read into the simulator; their count is the board's for every play.
- [ ] Replay: Reboot's later plays put the hand the board had into your draw pile (ShuffleDrawPileAction's cards are the board's).
- [ ] Ki Chant: the cards it looks at and discards stay in the simulator's draw pile (only the one it takes leaves). A made card into a full hand goes to the simulator's discard pile (round four: the one card a play makes, its profile from the game's card: `CardEventAction.MadeCard`); a play making several kinds isn't followed there yet.
- [ ] The play check's orbs channeled on a replay: the simulator's channels aren't in the play log yet (counted skip).
- [ ] Hand room outside the engine's rule (reviewed in `PerPlayRuleTests`): HandDelta's cards returned by hooks after the play; read them from the simulation.
- [ ] Test in game: Incoming Damage over a discovery mid-fight (it shouldn't cover the three cards).
- [ ] Test in game: Personal Hive's count on a multi-hit attack (Entomancer).

## 19. Game v0.113.1 (beta, 2026-11-14)

Decompiled and diffed against v0.113.0. Rule changes: Stretch now counts a card whose enchantment gains block (EnchantmentModel.GainsBlock, set by Adroit), so an Adroit card gets and spends Stretch; Show Off's upgrade no longer touches Aim (no effect). The rest is hover tips, visuals, the compendium and cloud saves (`.pre-repair` files stay local; run history counts only `.run` files), none of which the mods read.

---

## 18. Game v0.113.0 (beta, 2026-11-12)

Decompiled and diffed against v0.111.0 (`~/git/sts2-decompiled`, one commit per version). New: a character (the Fool: arrows that impale enemies, melodies Wah/Cho/Ki, tarot cards), the Dread run mode (12 boss Dominions), Blitz merchants, 17 relics, 41 powers, 121 cards, 3 potions, 5 events. 744 existing classes changed; the ones the mod's rules read were reviewed from their diffs.

- [ ] Test in game: the aim marker's look (size, corner, arrow direction) on a lethal skull and a crossed tab; a free-card `?` on Snecko Eye and Frantic Escape.
- [ ] Test in game: the Guide page (cards placed with room for their tabs, tabs drawn, flashing rows, the sums).
- [ ] **The Fool, still open:** ~~Timber's damage to every enemy on a kill~~ (0.35.1); ~~Blossom Shot's Stretch~~ (2026-11-22: `AttackAction.StretchFromDamage`); ~~Epic Retrieval's energy~~ (1.21.11, `CardEnergy`); ~~Motif's draws for the shuffle warning~~ (0.31.40); ~~what Lost Lute plays~~ (2026-11-22: the top of your draw pile, `AutoPlayFromDrawAction`); Covering Fire and Compost (teammates).
- [ ] **Decision #93: what a teammate doesn't get yet from the one engine:** their end-of-turn auto-plays (Simpleton's pick: their card plays built as theirs, decision #80) and their draw pile (`SimAdapter.FillDrawPile`, DrawPileMemory for their pile); the Death Skull on Teammate Incoming Damage (their `Died` is now tracked).
- [ ] **Replays of states with auto-play picks:** a report's start state with `AutoHand` plays (Stampede, Simpleton) doesn't load back (`SimAction` is abstract: no JSON type info), so those reports can't be added as replay cases yet.
- [ ] **Decision #95 follow-ups:** total Intent Forecast's odds (logged per roll, not totalled) and the lethal tab's own chances; events' odds and Event Reminders' "logged only" lines (after combat).
- [ ] **Decision #92: the gaps.** Each of these is a check skipped (rule 1) or an outcome not modelled (rule 2); the combat summaries count them, and each is closed by modelling it, until none is left:
  - Not modelled: Hellraiser auto-playing a drawn Strike whose play isn't built (aimed at a teammate, or every enemy you could hit can't die: the game caps those auto-plays); an end-of-turn auto-play from a hand that changed in a way not simulated (cards made or brought back; a drawn card whose play isn't built); an auto-played card whose play isn't built (aimed at a teammate).
  - Not checked (enemy turn): an extra turn (no enemy turn follows); an end-of-turn auto-play that changed your hand; an auto-play no branch made (another source); a teammate's end-of-turn auto-play.
  - Potions: a potion's tabs show the relics drinking it sets off, not its own outcome (a damage potion's kill, a block potion's effect on Incoming Damage).
  - Not checked (card play): a teammate's hand changes the play while Count teammates' hands is off (none today; decision #93);
  - Not checked (card play): tabs from a simulation not fully explored, or with a branch not modelled (re-check against the finished exploration instead);
  - ~~Not checked (enemy turn): a teammate's Stampede or Simpleton pick at end of turn~~ (2026-11-22: predicted from their hand for the checks; skipped only when their hand couldn't be read). What's shown doesn't mark it either: Incoming Damage, the Death Skull and lethal tabs should carry the ? while a teammate has Stampede (decision #92, rule 2).
  - Not checked (enemy turn): a branch the forecast didn't model; a forecast whose search stopped at a cap (decision #107).
  - Not checked (card play): another action during your choice; aimed at a teammate; tabs computed before another play finished; simulation pending; state after the play with auto-played cards, a pick's auto-plays at other enemies, no simulated play for its target, too many branches, or no simulation for the card.
- [ ] Decision #107 follow-up: play-test on busy boards (four Defects, multiplayer chains, multi-hit cards with orbs): how quickly tabs settle with every card's search running, whether the 100 ms waiting ? feels right, that nothing blinks. Read the `[perf] combat summary` lines and the `sim-cap` reports: whether either cap is ever hit, and the memory per state in real fights (about 2 KB measured in tests).
- [ ] Decision #107 follow-up: chains inside one step run separately on every branch (Beacon of Hope sharing block with Juggernaut, nested Then): with three or more such players a step makes millions of copies for a few hundred results, and the work cap stops it. Merge those chains across branches (advance every branch through each stage of the chain together) so they finish.
- [ ] Decision #107 follow-up: every card's search repeats the end of turn; cards whose play leaves the board in the same state could share it. Measure first.
- [ ] Check the Sentry quota the session summaries use; and once 1.26+ has hours of play, whether its slow mod frames (275 an hour on 1.24.6, over half an hour of launches) are real.
- [ ] Test in game: the draw chance tab's text and corner number on Escape Plan, Pillage, Scrape (hand and Guide page), and that other tabs on a reused tab node don't show a stray corner number.
- [ ] Test in game: the Ethereal glow mask (the glow rings Void Form instead of showing through; it still pulses; no shader error in the log), and the Performance page's memory column after a fight.
- [ ] Test in game: memory per frame in fights with the board probe (the Performance page's memory column), and no `[perf] board probe missed a change` in the log.
- [ ] Test in game: the settings menu's new sections (Chances, Kinds of tab, Reminders by place), greyed-out options when a feature is off, and Show chances off in a fight (no chance on the Death Skull, lethal tab, draw chance tab or Intent Forecast).
- [ ] Gap: Scrape discarding a Sly card plays it (not simulated: `?`).
- [ ] Tests: the simulator files coverage shows least run (`./scripts/coverage.ps1`, 2026-11-21: 90.6% overall): `Sim/Models/BlockModels.cs` (35%: Pael's Legion, Vambrace), `Sim/Models/EnemyModels.cs` (28%), `Sim/Models/ProcRelicModels.cs` (76%). A scenario test per untested hook, from the decompile.
- [ ] Tests: mutation testing (`./scripts/mutation.ps1`) on the simulator's core files, file by file; `SimChance.cs` scored 72% (2026-11-21).
- [ ] Gap: a Sly card discarded from your hand by another card without a choice (the whole hand: Storm of Steel, Calculated Gamble, Shadow Step, Wheel of Fortune) plays itself (CardCmd.Discard, AutoPlayType.SlyDiscard): such a play is `?` (decision #146). Model it as an auto-play at a random target (the Stampede plays, played after the discards and their draws). A discard you pick (Acrobatics, Survivor, Prepared...) is a card, never a particular one (2026-11-23, the owner's rule): a Sly card playing itself then is your own decision, neither simulated nor marked, and the Play audit excuses it ("the card you picked played itself (Sly)", counted).
- [ ] Gap (the chosen half decided 2026-11-23: a pick is a card, never a particular one; what depends on which card is neither simulated nor marked): which cards a random discard or exhaust takes isn't kept, only how many (decision #146): a Burn, an Ethereal card or a card Stampede could pick is still counted in your hand after Survivor or Cinder.
- [ ] Idea (owner): a crossed tab on cards for end-of-turn relics that count your hand (Cloak Clasp's block, Obsidian Vise's damage): playing a card loses one card's worth. Every card would carry it while you hold one; decide the design first.
- [ ] Decision #108 follow-up: use the dashboard in a few real sessions; prune what nobody looks at, and add what was missed (time from a board change to every tab settled per board, the adapter's own time per frame apart from its feature).
- [ ] Decision #177, third session: read the steady table again. Rest Site Preview at the fire (about 1.8 KB a frame) is timed in parts now (rest-preview:read, :hover, :key). Keyword Tags and Ethereal Fade (small, now and then; one feature, Card Indicators, until decision #185). The settings menu's Guide and Setup pages (about 14 KB a frame while open). The Battleworn Dummy fight's Fiend Fire predicted no Charon's Ashes damage: retest with the exhaust-first order.
- [ ] Check in game (decision #254): the first fight's first frame in the dev log (`[perf] slow frame`), and the `[load]` line for the fight code (its time on the worker). What's left on the first board is the game's own code running for the first time (its intent damage math, its powers), about 16 ms for the board, 5 for the intents and 8 for the hand's plays measured cold, only on the game's thread since it reads the game's objects; the first settings menu open (58 ms) and the dev dashboard's first frame (28 ms) may also come down with the mod compiled ahead.
- [ ] Check in game: the settings menu's steady line gone (if `settings-menu:examples` still shows garbage, it's the Guide's or Setup's examples: the Relic Counters rows' paint or a shown card that doesn't settle).
- [ ] No Draw and Fiddle at the end of your turn (Dark Embrace's saved Ethereal draws): the simulator stops draws only inside a play.
- [ ] Decision #258 follow-up: the first sessions with Memory tracing on: read the Performance page's "What does tracing cost?" (Memory against Off) and the traces' sizes, and decide whether Memory stays the default. Its verbose GC events include one per object finalized, each with a stack: if that's what costs, a dotnet-trace with event-ID filtering for the runtime's provider could leave them out. Check in game that the stop event ends a recording cleanly (the rundown written, the file read by `scripts/trace-report.ps1`) and that the exit leaves no dotnet-trace running.
- [ ] Pipeline gaps found by the check: a player's Intangible flash on an enemy hit (AfterModifyingDamageAmount); Covered and Guarded ending when the teammate who gave them dies (AfterDeath); a player's death and Fairy/Lizard Tail settled after AfterDamageGiven/Received, with AfterDamageReceived skipped on a killing hit (the game's order); a discarded Sly card's auto-play (ShouldPlay, BeforeCardAutoPlayed, already `?`).
- [ ] Pipeline follow-ups: every damage to a player is one landing now (`SimEngine.Lands`, decision #241); AfterDamageReceived on a killing hit on an enemy (the game skips it); Snecko Skull with Unsettling Lamp (additive before multiplicative in ModifyPowerAmountGiven); enemies' attacks running BeforeAttack/AfterAttack (Painful Stabs, Skittish, Suck).
- [ ] Kunai, Shuriken and Game Piece read their values by the game's keys now (decision #182, part B2): no change in play today (each value is 1, the fallback the wrong keys gave), but a value a game update changes is followed.
- [ ] Dark Embrace at the end of your turn: the game draws for the Ethereal cards exhausted together (AfterSideTurnEnd), the simulator as each one exhausts (same count; a drawn Burn could act in the simulation and not in the game).
- [ ] Test in game (decision #185): an old settings file (Card Indicators off, Run Reminders off, a place's group switch off) loads into the new switches as it was; the Outside fights page (four switches, the note naming those off, Inspect and the event chance under Event Reminders); Quick setup's Between fights rows; the dashboard's names, and ids on hover.
- [ ] Test in game (decision #186): the Tags by source filter row and a search across sources (headings, column headers, All on / All off per source), Quick setup's group switches, the On your cards section greyed out with Tags on cards off, and the Guide's new order.
- [ ] Test in game (decision #187): the tag art icons (`CardOverlayRenderer.Standalone`: size, the part a card covers cut off, the slash, the text tags 2× and 40%, the overlapping family icons), the teammate portrait, the % corner on the chance icons, the column alignment of the indented source rows under their headers, the Tags by source button under Tags on cards.
- [ ] Drum of Battle with several cards exhausted: the game runs each card's exhaust and its reactions in turn; the simulator all exhausts, then Drum's energy (differs at 0 energy when Dark Embrace draws a Void). One exhaust action per card.
- [ ] Cards a play returns to your hand (Make It So, Right Hand Hand, Whistling Arrows) never enter the simulated hand: the hand's size, room for later draws and Regret's count miss them.
- [ ] Withering Presence: a Wither made into your hand during the turn isn't added to the cards acting at the end of the turn (its damage isn't simulated); then Aeonglass upgrading it as a monster listener.
- [ ] Ethereal cards exhausted at the end of the turn don't lower the simulated hand's size.
- [ ] Monologue replayed while a copy is already there: the replay's new copy gets its Strength after it lands, so the total misses it by one (keep Monologue's copies apart, like the counter powers).
- [ ] The value-key scan on main's models, and the adapter's own value keys (`Sts2State.Var` in `SimReaders.Read`) in a table the scan reads.
- [ ] A replay's later plays see your hand as it was before the first (Restlessness, Impatience, Scrawl replayed): the cards the first play draws aren't in it.
- [ ] A pick's discards on a replay's later plays aren't counted (Acrobatics, Survivor, Prepared replayed: one discard, the later plays none; `PlayFacts.DiscardCount` with `pickEachPlay: false`, as the table it replaced did).
- [ ] A made card's pile passed by a game helper after a `??` isn't read (Shiv.CreateInHand: `creator ?? owner` leaves the stack unread at the join); it goes where the card's own code's piles go (`CardCode.MadeInto`: the hand today).
- [ ] Play check, counted gaps since decision #194: a play with no finished simulation at the target it was played at (pending, or stopped at a cap) has its budget (draws, exhausts, discards, created cards, orbs channeled) unchecked, and a card that channels or evokes orbs its block and unsourced damage unchecked; each is counted with its reason (`budget`, `block`, `damage to enemies`). Before, PlayFacts' counts on the board stood in. Pillage's draw budget is its simulation's most (PlayFacts' "a full hand" bound is gone): confirm in game that a long Pillage chain isn't reported as unexplained draws.
- [ ] Skip reasons the play and forecast audits still count (decision #298, the owner's rule: everything short of a capped search is checked). A pending simulation is finished in the background now; verify in game that the combat summary's "simulation pending" count drops to the plays still running after 600 frames. Each group below is a gap to close:
- [ ] The fight's end, what's left (decision #301): the play check's history shortfalls of a winning play (damage to enemies, block, cards made: "the game logs nothing from the killing hit on") need the damage read from the enemies' HP before and after instead of the history; the power tabs' "didn't fire" and "drawn" checks (`ProcAudit.Powers`) and Razor Tooth's upgrade check still skip a play that ended the fight; a player's death (CombatTurnState.PendingLoss, IsCombatEnding's other way) doesn't stop the simulation yet; and within one action the simulator asks the fight's end only at its start (a multi-target damage after the last kill, a listener's own effects inside a hit). Verify in game: a winning play's checks report nothing new (Sunder, Compost, Twine, an Exploder killed last).
- [ ] The shown-tabs check (tabs in hand against the play's start) skips "a simulation not finished in time" when the hand's simulation was pending: needs that frame's simulation finished in the background too and the fight log's count at the play's start kept, to compare once it's done.
- [ ] The forecast audit skips "the forecast was still on its way as the turn ended" and "the forecast had no result by the enemy turn": needs the forecast's search finished in the background from its start state, as a play check's is now (decision #298).
- [ ] Auto-plays inside a play ("auto-played cards", "random auto-plays", "fired during random auto-plays", "the pick's auto-plays hit different random enemies", "after random auto-plays or a draw-reactive source"): needs `CardCmd.AutoPlay` simulated (its own item) and the auto-played card's identity per branch, so the play's outcome is held to the branches that played the same cards.
- [ ] Draw-reactive sources ("fired on a drawn card; no simulation to tell", ProcAudit.DrawReactive's names): needs the drawn card's identity per branch held to what was drawn (the history's CardDrawnEntry), as the forecast audit keeps the auto-plays' branches.
- [ ] "fired after a random hit or kill; no simulation to tell", "an aimed tab, with no complete simulation of the target played at", "its simulation has no finished outcome at the target it was played at", "no outcome for the target it was played at", "no simulated play for its target": a play whose simulation at its target is missing; with the pending one finished, what's left is a target the card's simulations didn't cover (a random target's roll, a target that wasn't hittable as the prediction was made): needs the play's simulation at the target it was played at built when it's missing.
- [ ] "another action ran during your choice": needs the interleaved action's history entries and flashes taken out of the play's window (its own window), so the play's snapshots are its own.
- [ ] "aimed at a teammate; the simulator applies it to you" (Ignition's Plasma): needs the card's effects on the teammate it's aimed at (their orbs, their powers) in the simulation.
- [ ] "its tabs were computed before another play finished": a queued play's prediction is rebuilt as it begins, so this is a play made before an earlier one finished whose action began first; needs the prediction rebuilt again once the earlier play's action finishes.
- [ ] "an extra turn (no enemy turn to compare)" (The Hanged Man): needs the forecast held to the extra turn's own end and the enemy turn after it.
- [ ] Multiplayer skips ("a teammate's hand changes it; Count teammates' hands is off", "a teammate's end-of-turn pick from a hand not read", "a teammate auto-played a card at end of turn"): the shown forecast leaves teammates' hands out by design (decision #25); needs the audit to hold the full-knowledge simulation (with their hands) instead, as the play check does.
- [ ] Capture failures ("the powers before or after the play weren't captured", "your relics before or after the play weren't captured", "not captured", "couldn't read the state", "no simulation for this card"): each is an exception reported on its own; needs the cause fixed where one shows up.
- [ ] The player's own picks inside a play ("your pick inside the play exhausted a card that acts when exhausted", "{card} came back to your hand"): what the picked card does is the player's decision (decision #167), not predicted; needs the pick's card held as a branch per card in hand to check it.
- [ ] "a payout decided as your turn ends: the forecast check holds what it pays" and "a payout the play sets up, paid after the play": checked by the forecast audit instead of the play check; reviewed, nothing to close unless the forecast audit misses one.
- [ ] Card commands the simulator can't follow (`SimAdapter.CommandsNotTranslated`, decision #292): each makes the play's outcome unknown where it runs (`?` and a range, counted by the audits with its reason). Each below is a gap to model; `StepTranslationTests` fails a command on that list with no open item here.
- [x] `CardCmd.Upgrade` (Abundance, Apotheosis, Armaments, Begone, Charge, Compact, Dirge, Drain Power, Florescence, Guards, Hidden Daggers, Jackpot, Knife Trap, Largesse, Manifest Authority, Primal Force, Quasar, Reave, Splash, Stoke, Storm of Steel, The Lovers, The Moon): translated by what it upgrades (decision #347): your piles' cards as `UpgradeCardsAction`, followed where a later step reads them; a card the play makes, a pick and an auto-play's callback no action. Knife Trap+ keeps its `?` for its `CardCmd.AutoPlay`.
- [ ] `CardCmd.Upgrade` of a card a later step reads (decision #347): a hand card that can be upgraded and acts as the turn ends (Burn's damage, an Ethereal card's exhaust), one Stampede or Simpleton could auto-play, a card drawn or played from an upgraded draw or discard pile whose profile isn't plain, a card playing itself from an upgraded exhaust pile (Howl from Beyond), I Am Invincible on top of an upgraded draw pile: each is unknown. Needs each such card's upgraded copy read at the board (its turn-end card, Ethereal, profile, auto-plays), the hand's turn-end cards each with its hand id. A card upgraded in any other shape (not one the play makes, a pick, or one of yours in a pile: `ShapesNotFollowed`) is unknown too; none today. A drawn card that acts when it's exhausted (Drum of Battle) isn't among the cards an upgrade could change.
- [ ] `CardCmd.AutoPlay` (Beat Down, Catastrophe, Cho Chorus, Decisions Decisions, Eidolon, Knife Trap, The Fool, Uproar): a card in hand or a pile played by the card. Needs the played card's own program run inside the play (the auto-play from the draw pile is already: `AutoPlayFromDrawAction`), its pick a branch per card when the code picks at random.
- [x] `CardCmd.ApplyKeyword` (Sculpting Strike, Snap), `CardCmd.ApplySingleTurnSly` (Hand Trick), `CardCmd.ApplySingleTurnRetain` (Expertise, main): followed by what the keyword changes this turn (decision #348): a picked card made Ethereal exhausts as the turn ends; Retain, Sly, Exhaust and Innate change nothing before the turn is over.
- [ ] `CardCmd.ApplyKeyword`, `CardCmd.ApplySingleTurnSly` and `CardCmd.ApplySingleTurnRetain` in a shape not followed (decision #348): a keyword given to a card other than one you pick or the play drew, or Unplayable or Eternal given: unknown; none today. Not simulated (decision #188): a card made Ethereal that acts in hand as the turn ends (it doesn't exhaust), and a card made Sly that a discard later in the simulation takes (an auto-play at the turn's end discarding your hand).
- [x] `CardCmd.Enchant` (Blade of Ink): an enchantment given to a card the play makes is no action when its class acts only on its card's play (decision #349).
- [ ] `CardCmd.Enchant` in a shape not followed (decision #349): an enchantment given to a card other than one the play makes, or one whose class answers a hook while its card sits in a pile (a draw, the flush, a shuffle, another card's play): unknown; none today. Needs the enchanted card's enchantment as a listener in its pile.
- [x] `OrbCmd.Passive` (Darkness): each of your orbs of a class set off with no target runs its own read Passive (decision #214).
- [ ] `OrbCmd.Passive` at a creature (Tesla Coil's Lightning passives at its target, decision #214): unknown. Needs the orb's read Passive to take its target (the reader reads `target` as none, as `TriggerPassive` passes; a hook value for the target given and a damage target "the target given, else a roll").
- [x] Red Skull's threshold after a max HP change in a simulation: worked out on the max HP as it stands from its HpThreshold value (`RedSkull.ModifyStrengthIfNecessary`'s `MaxHp * HpThreshold / 100`), so Brightest Flame's loss, Paper Cuts' or a max HP gain moves it; the HP the adapter read stays the fallback with no value or max HP read.
- [x] An Osty heal (`SimEngine.HealOsty`, CreatureCmd.Heal of him) and a summon's heal tell `AfterCurrentHpChanged` his gain (`SimEngine.TellHealed`), as a player's does (decision #342).
- [x] `ArrowCmd.GainArrowSlot` (Quiver): a slot for the card's owner, its arrow there to shoot (a new ArrowSlot starts Retrieved), one more arrow in the simulator's state each round of its loop.
- [ ] `ArrowCmd.GainArrowSlot` for a player other than the card's owner: a shape not followed (unknown); none today.
- [ ] `PotionCmd.TryToProcure` (Alchemize) and `PlayerCmd.GainGold` (Hand of Greed's kill): what reacts to a potion procured (Belt Buckle's AfterPotionProcured) or gold gained (Dragon Fruit's AfterGoldGained) isn't followed. Needs their hooks as read steps; with no listener, nothing to follow (a reviewed reason then, per listener).
- [ ] Translated commands in a shape their translation doesn't read (`SimAdapter.ShapesNotFollowed`, decision #300): `ArrowCmd.DislodgeArrow` out of anyone but the card's target, `CreatureCmd.Kill` of anyone but your Osty or each of a list its code works out (End of Days' KillEachAction, decision #310), `CreatureCmd.SetCurrentHp` and `CreatureCmd.Stun` of anyone but the target, `CreatureCmd.LoseBlock` from anyone but the target, `PlayerCmd.GainStars` for anyone but the card's owner, `PowerCmd.Remove` from anyone but the card's owner (bar the target's Artifact with its block, Expose's), `DamageCmd.Attack` aimed at a creature its code picks by an order other than the enemy you can hit with the least HP (Memento Mori's, read as its own targeting: decision #320). Each is unknown where it runs (`?` and a range); `StepTranslationTests` fails a card on either branch that calls one (none does today). Also unknown, with why, where a translation can't name whom a step acts on (damage, block, a power, energy, a heal, a summon to a creature its code picks), an attack's targeting, a channelled orb or a make it can't read. Each one a card brings is a gap to model.
- [ ] Decisions, Decisions: one simulation of the whole play, the card's draws before your pick, the pick's auto-plays, and what the card's finish sets off (Black Hole on its stars, its count toward Pale Blue Dot), so the play check can hold its state and powers after it. Skipped today with a reason (`ProcAudit.PickApart`). No tab for the card itself (the owner: what to triple is the player's call).
- [ ] Decision #172 follow-up: check in the first dev session that the engine's pipeline-compilation counters are running totals (`compiles` stays small and mostly 0 once the game has settled); read the next unexplained stall against them and the hook times.
- [ ] Decision #108, phase 3: a dev-only flag hotkey that bookmarks the last events and the simulator's start state for a note later; exporting a flagged or reported start state as a replay case into `tests/SpireQoL.Sim.Tests/Cases` (the page can write there once given the repo folder).
- [ ] Decision #87 stage 4: Incoming Damage's distribution (each amount's chance) on hover.
- [ ] Decision #87 stage 2 follow-up: whether lethal chance tabs are too many in roll-heavy fights (Lightning, Juggernaut): they show whenever the card surely raises a death's chance; a floor (say 25%) may be needed.
- [ ] Decision #87 stage 3 note: drawing into a full hand stops the draw (the card stays in the draw pile: CardPileCmd.Draw), while a card added to a full hand goes to the discard pile (CardPileCmd.Add: Shivs, generated cards). Draw reactions (Iteration: a Status drawn draws more) run inside the draw, before the next card: with hand space short, order matters there.
- [ ] Decision #107, numbers to set from the `[sim] searched` and `[sim] cap:` logs: SimJob.FirstBudget (20,000 copies before a background turn), the background share of cores (half), the memory cap (150,000 states held together) and the work cap (3 million copies a search).
- [ ] Play test: Rest Preview's sum beside your character (and a teammate's with Mend); free-card `?` and crossed tabs.
- [ ] Test in game: the chance under event icons (Colorful Philosophers, Infested Automaton, Room Full of Cheese, Sea Glass); Mystic Lighter beside Tezcatara's soup.
- [ ] Test in game: boost tabs on a card reward, the shop and Sea Glass's grid; that they leave with the screen and never sit on deck-pick screens.
- [ ] Test in game: Inspect at Colorful Philosophers, Room Full of Cheese, Glass Eye and a transform (Pandora's Box, an event transform); that Esc closes it and the game's keys are blocked while open.
- [ ] Look across runs: `[audit] [odds]` lines (predicted chance against how often a card came up).
- [ ] Test in game: the Guide page's Rest Preview, reminders examples (Reward Tags, Event, Shop and Map Reminders) and the green 0 on the free-card examples.
- [ ] **Dread mode:** boss stats and moves change in Dread runs (read live from intents); Dominion powers reviewed in the ledger. Queen's Dread opening applies 99 Frail, Weak and Vulnerable.

---

## 17. Open display decisions (2026-11-10, from play testing)

- [~] **Showing deaths that happen anyway** → Death Skull (in test). Example: Punch Construct at 2 HP with your orbs about to finish it at end of turn: no skull on your attacks (the lethal tab means "this card makes the kill happen"), but now a skull beside its HP. Branch weights in the simulator give the chance when a roll decides it.
- [ ] Multiplayer: confirm in a co-op game that the end-of-turn check compares the last frame after every player has ended.
- [ ] Multiplayer step 2 leftovers: cards you play that change damage to players (~~Tank on you~~, decision #329; ~~Intercept on a teammate~~, 0.37.4); a teammate's Osty and their Fading/Feel No Pain block (from their hand: not in what's shown, decision #25); per-teammate checks of block and Osty.
- [ ] Multiplayer: plays resolve one at a time through a shared queue. A teammate's queued, unresolved play can change what yours does (Beacon of Hope → their Juggernaut on your block card), and latency widens the gap between what's on screen and what resolves. Tabs for effects through teammates (their Juggernaut from your Beacon), and treating teammates' queued plays like your own quick plays (logged, not reported, unless the history shows otherwise).
- [ ] Test in game: a quick play's check at resolution (two enemies, Sword Boomerang then a queued Strike; the summary's "against the tabs in hand" count should stay 0).


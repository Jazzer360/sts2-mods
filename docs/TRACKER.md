# Tracker

The master work list for Spire QoL. Check an item off only when it is implemented, documented in [FEATURES.md](FEATURES.md), and noted in [CHANGELOG.md](../CHANGELOG.md).

Everything below was found in the decompile of game **v0.111.0**. Class names are the game's own (`MegaCrit.Sts2.Core.Models.*`). Thresholds come from each class's `CanonicalVars`. Re-check them after game patches.

Legend: `[ ]` to do · `[~]` built, needs an in-game check · `[x]` done · **(verify)** behavior inferred from code, confirm in game.

---

## 0. Core: settings and layout (`core/`, used by every feature)

- [~] **Settings storage**: per-feature on/off and options, saved to a JSON file in the mod's own folder (not in game saves). Survives game updates.
- [x] **In-game settings menu** → 0.23.0: F10, plus buttons on the Mods screen (Spire QoL selected) and the pause menu. The game has no mod-settings hook, so it's our own layer; game hotkeys are blocked through NHotkeyManager.
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

- [x] **Decide: keep the Sly tab?** (2026-11-09) Kept, on by default (decision #77). It doesn't fit "happens when you play this card". It helped when choosing cards to retain before Well Laid Plans changed; the game already makes Sly cards glow when you pick cards to discard. Stays for now.

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

- [x] **Bound** (Chains of Binding): one Bound card per turn. The game crosses out the cost of the others.
- [x] **Ringing**: the game crosses out the cost once it can't be played.
- [x] **Smog** (Smoggy): the game crosses out the cost once it can't be played.
- [x] **Entangled** (Tangled): attacks cost +N. The game recolors the cost; nothing to add.
- [~] **Hexed** (Hex): grants Ethereal → handled by translucency. Confirm the overlay and translucency look right together.
- [~] **Galvanized** (Galvanic): playing the card deals N damage to you. Crossed Galvanic tab (Proc Indicators).
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
| [~] | Razor Tooth | attacks and skills that can still be upgraded | upgrades the card when played |
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
- [x] **Infection**: blockable.
- [~] **Toxic**: blockable.
- [~] **Wither**: blockable.
- [x] **Constrict** (debuff, AfterSideTurnEnd): blockable (`Unpowered`), gone when its applier dies; a model (1.2.2).
- [x] **Magic Bomb** (debuff, AfterSideTurnEnd): blockable, only while its applier lives, then removed; a model (1.2.2).
- [x] **Disintegration** (debuff, AfterSideTurnEndLate): blockable; a model (1.2.2).
- [~] Unblockable, listed separately: Regret (HP loss = cards in hand), Bad Luck, Beckon, Demise. Poison is already previewed by the game.
- [ ] Not damage, but worth a reminder: Doubt (Weak), Shame (Frail), Debt (lose gold).
- [~] **Doesn't count enemies Poison kills before they act** (with Hard To Kill per-hit cap and Hardened Shell per-turn cap). Doom does NOT prevent attacks.
- [~] **Retaliation cancels remaining hits**: Thorns (before each hit; the killing hit still lands), Flame Barrier and Reflect (after each hit), through the game's damage hook.
- [ ] **Health-bar preview** (the summary line now sits under the HP bar; a bar overlay is still open): render the expected loss after block alongside the game's existing Poison/Doom preview in `NHealthBar`.

---

## 6. Shuffle Tag → `shuffle-warning`
- [x] 1.11.1: Ki Chant's own shuffle keeps your hand out of the draw pile (only Reboot puts it in), its pick takes hand room, its looked-at cards leave the draw pile (auto-report: Motif's draws past a full hand).

- ~~Draw-pile counter~~: dropped in 0.3.0 (the card badges are enough).
- [~] Discard pile tab on draw cards in hand whose draw would exceed the draw pile (forcing a reshuffle).
- ~~KEPT marker on other hand cards~~: dropped in 0.12.0 (the draw-pile tab is enough).
- [x] Every draw card: all `CardPileCmd.Draw` callers, including Prepared, Dagger Throw, Escape Plan, Pillage, Scrawl, Compile Driver, Restlessness, Huddle Up, Mad Science (Wisdom).
- [ ] Draw potions: a shuffle tab under potions whose draws reshuffle (potion tabs exist since 0.41.0).

---

## 7. Intent Forecast (built in 1.12.0)

Knowable information only (decision #72): an enemy's next moves where its move rules decide them (a fixed pattern), and the odds of each possible next intent when the pick is random, worked out from its move rules and the moves you've seen it make. Never the result of a random pick before the game shows it, a later move the game rolled ahead of time, or the RNG.

Design (decision #72): on hover only; looks ahead until a roll (every certain turn, then the first random pick with each move's chance).
- [x] Monster AI structure: `MonsterMoveStateMachine` of `MoveState` (fixed follow-up), `RandomBranchState` (weights, `MoveRepeatType`, `maxTimes`, `cooldown`) and `ConditionalBranchState`; the next move is picked when the current one ends.
- [x] Deterministic next moves (1.12.0).
- [x] Random branches with odds (1.12.0).
- [x] Conditional moves, as things stand (1.12.0).
- [ ] Test in game: the hover panel (shown on hovering the intent, stacked above it (1.13.1), NIntent nodes' size and animation, the chance labels), a stunned enemy, a hidden intent.

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
- [x] Builds on the per-relic `Relics` table and the per-feature `Features` table that exist now. An in-game menu (§0) makes these usable without editing the `.cfg` file.

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
- [x] Removed the Goopy "added Exhaust" tab and the replay-enchantment double-play tab (the card text already shows both).
- [x] Forecast: random-target end-of-turn damage with several enemies (Lightning, Parrying Shield, Forgotten Soul) stays uncounted by design: we can't know which enemy it hits, and a kill we can't be sure of would hide an attack. Consuming Shadow's evokes are modeled in 0.23.4 (Frost block, Dark on the lowest-HP enemy, Glass on all, Lightning random).
- [x] Tags: enemy death effects that revive (Adaptable, Illusion, Reattach): checked, no change. Their death still runs the normal death hooks (`wasRemovalPrevented` false), so Gremlin Horn and on-death powers fire as tagged, and the revive replaces their next move (no attack). Attack hit counts beyond the Repeat value: fixed in 0.23.4 (`PlayFacts.HitsPerPlay`: fixed counts, X costs, calculated hits, Dismantle, Spite, Fiend Fire, Mad Science).
- [x] Read the first audit summaries and fix what they show (2026-11-09, 0.23.6: the first 16 reports were all audit false alarms: window overrun on quick plays, cards' own powers applied in code, Thorns on an untargeted enemy, Catastrophe's random auto-plays). Keep reading new ones.

---

## 11. Automatic bug reports (2026-11-09) → 0.22.0

- [~] Mod: Sentry reporting of crashes and audit mismatches with a full state snapshot (`ReportErrors`, on).
- [~] Workflow: Sentry → GitHub issues (`auto-report`), resolve on close, reopen on regression.
- [x] Sentry project created; DSN in `ReportingConfig.cs` (0.22.1).
- [x] Repo secret `SENTRY_AUTH_TOKEN` and variables `SENTRY_ORG` (derek-jass) / `SENTRY_PROJECT` (spire-qol) set; sync verified (2026-11-09).

---

## 12. Settings menu (2026-11-09) → 0.23.0

- [x] Menu: General, Card tabs, Relics, Your powers, Enemy powers; icon + name + what the tab / tracker shows; search; this-fight filter; All on/off.
- [x] Live feature on/off (OverlayHost.FeatureEnabled; widgets of a switched-off feature hide).
- [x] `Relics` = tracker only; relic tabs via `Tabs`; turn timers per relic (migrated).
- [x] Checked in game (main menu, Mods screen button, every page, Esc, toggles saved).
- [ ] Check in combat: pause-menu button, toggling a feature / tab mid-turn, "only what's in this fight".
- Standing rule (CLAUDE.md): keep `Menu/SettingsCatalog.cs` in sync with new proc rules and tracker specs (the log warns on unknown tab keys and missing tracker rows).
- [x] 0.23.1: removed the "can't be played" tab and dimming; the game's crossed-out cost covers every case (`NCard` unplayable cost icon on any non-cost `CanPlay` failure). Crossed tabs now only mean "might be bad for you".
- [x] 0.23.2: settings menu Reset defaults button, game cursor, discard pile icon on Shuffle Warning (loaded from `res://images/packed/combat_ui/discard_pile.png` outside combat).
- [x] 2026-11-09: "knowable information only" made a hard rule (CLAUDE.md). Audited the mod: no RNG or seed reads; random effects show amounts, never targets; enemy attacks come from the intent on screen; private reads are only counters and "already used" flags a player could track.
- [~] 0.23.5: tab icons centered in the visible part of the tab (they sat 4% of the tab length too high).
- [x] Sentry sync: a brand-new issue whose event isn't indexed yet no longer fails the whole run (skipped, retried next hour).
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
- [x] 0.23.19: audit uses unfiltered predictions (`CardDecor.Predicted`); richer reports (full numbered window with flashes and markers, own entry text, card facts, hidden tabs, split candidates, orb values).
- [x] 2026-11-09: docs/DEVELOPMENT.md (setup on a new machine, day-to-day loop, error reports), `scripts/decompile.ps1` (recreates ~/git/sts2-decompiled), `scripts/game.ps1` (in-game test helper); working rules for auto-reports in CLAUDE.md.
- [~] 0.23.20: Juggernaut from end-of-turn block gains in the end-of-turn model (random target: counted with one enemy).
- [~] 0.23.21: potion effects out of play windows (#67: Power Potion's free Creative AI counted as a created card). `PotionModel` writes `PotionUsedEntry` after `OnUse`, so the 0.23.10 cut at the entry left the potion's effects in the previous window; the gap before the next play now stops at the entry and the previous window's end is a best fit before it (`ProcAudit.PotionCut`).

---

## 13. Outcome simulator (2026-11-09) → 0.24.0

- [x] Pure simulator (`Sim/SimState.cs`, `Sim/SimEngine.cs`) with branching over random targets, merging, worst case / guarantees; offline tests (`tests/SpireQoL.Sim.Tests`, CI workflow "Simulator tests").
- [x] Adapter from the game (`Sim/SimAdapter.cs`); Incoming Damage (`TurnEndModel`) and the forecast audit on it.
- [x] Lethal tab (skull emote icon), Imbalanced and Asleep/Slumber tabs on it.
- [x] Check in game: Stone Armor + Juggernaut, Juggernaut card + Orichalcum, two Lightning orbs vs two low-HP enemies, a Defect fight's Incoming Damage. (2026-11-21: superseded by the simulator's tests, the golden fights and every play's check.)
- [x] 0.25.0: outcome audit in the forecast audit (deaths before acting, HP and hits through block at turn end, Imbalanced), which also checks the lethal tab through the end-of-turn prediction.
- [x] 0.27.0: kill-reactive tabs (Gremlin Horn, enemy death effects) from the simulator's kills during the play.
- [x] Logic review of the simulator against the decompile (2026-11-09): fixed Beating Remnant carry-over and Buffer order, Ethereal exhaust before hand damage, Glass evoke shrinking, Gold-Plated Cables by position, Infused Core on new Lightning, Tempest+, lethal = dead before acting, powers before relics, Disintegration after Parrying Shield, Imbalanced full-block rule, Hardened Shell and wake counts. Regression tests in `SimReviewTests.cs`.
- [ ] Remaining approximations: poison TriggerCount, a card's procs timing relative to its hits, block remainder across gains.
- [x] 0.24.1: Glass orb decays its base (`GlassOrb._passiveVal`), not the Focus-included value (tests in `SimReviewTests`).
- [x] 0.25.0: simulator poison ticks on stunned enemies (found while writing the outcome audit; test `PoisonTicksOnAStunnedEnemyToo`).
- [x] 0.25.1: simulator start state (JSON) in forecast/outcome reports, replay harness (`ReplayTests`, `Cases/*.json`), `[audit] [outcome]` log line per enemy turn.
- When a report is resolved, note here which layer it was: simulator rule / adapter / audit comparison.
- [x] 1.2.1: Conductor's Baton's power (simulator rule: the relic didn't apply it); Vigor, Aim, Quietus and Gigantification reserved for an attack under way (adapter: read as bound outside the simulation); Cho Barbs' temporary Thorns read twice (adapter); the after-play intent compared from the start state's hits, not the predicted state (audit: Weak, Tainted, Conductor's Baton); Possess Strength's Strength returned on death and Show Off's flash (audit); Reptile Trinket's potion tabs lost before the potion was logged (audit); Crystal Sphere's minigame and curse cards added with the plain add-card command (Run Reminders' code reading); Steady Cadence (card scan). Flutter's last stack on Wah Ki's first hit is already right since 1.0.0 (test added).
- [x] Wuppy: a hit bigger than Wuppy's HP carries its overkill on to your block and HP (`CreatureCmd.Damage`: `damageRemaining = OverkillDamage`), and your Thorns reacts to hits Wuppy takes (`ThornsPower.BeforeDamageReceived` sees you as the target) (1.2.2).
- [x] 0.26.0: chained draws (`PlayFacts.ChainedDraws`: Dark Embrace, Joss Paper, Game Piece, Iron Club, Vicious, Centennial Puzzle; hand limit; No Draw/Fiddle) for Shuffle Warning and draw procs; Biiig Hug shuffle tab. Suggested in game (Burning Pact + Dark Embrace).
- [x] 0.26.0: audits: shuffle check (pile events), state after each play vs the simulator's prediction of it, forecast "no single branch" joint check.
- [x] 0.26.0: simulator: Envenom, Snecko Skull, poison stacks/ticks, Weak and Enrage intent changes, Curl Up / Sneaky / Skittish block, Plasma values; full-state cache key (`SimState.FullKey`; the branch key missed intents and powers); parallel card simulations.
- [x] 0.29.0: Crab Rage / Ravenous on ally deaths, enemy Strength cards, Bouncing Flask's random poison, Biiig Hug's Soot as a created status, Calculated Gamble (draws, discards before drawing). Not modelled: Malaise (X-cost Strength loss), Strength from enemy powers mid-turn other than Enrage.
- [x] 0.27.0: proc audit by game action (`PlayCardAction` via `ActionExecutor.BeforeActionExecuted`, `CombatHistory.Changed`, `GameAction.AfterFinished`); removed the energy-spend split, 0-cost joint fit, potion cut and idle capture. Shuffle prefix on `CardPileCmd.Shuffle` (first observe-only patch, DECISIONS #10).
- [x] 0.27.0: `Sim/PlayChain.cs` (draw chains, pure, tested); 340 game class names compile-checked (`nameof`); missing private members reported (`Sts2State.MissingMember`); docs/DECISIONS.md.
- [x] Check in game: the action-based audit windows (Burning Pact's choice pause, queued plays, potions mid-turn, Catastrophe auto-plays). (2026-11-21: covered by the reports since: Burning Pact's choice, queued plays.)
- [x] 0.28.0: one model of a play finished (DECISIONS #12): being-hit enemy powers in the simulator (Burrowed, Flutter, Plow, Shriek, The Gambit, Slippery); Juggernaut, Sneaky, Enrage, Hand Drill and the being-hit tabs from the simulator; dead PlayFacts helpers removed (Unblocked, BreaksBlock, UnblockedWithOrbs, OrbBlockGains). Tests in `SimBeingHitTests`.
- [x] 1.25.0: the branch key hashes every player the same way (you had fewer fields than a teammate: energy, Focus, orb slots, the turn's card counts...), and every orb and enemy number; `SimKeyCoverageTests` changes each field of the state in turn and fails on one the key leaves out without a listed reason.

---

## 14. Coverage (2026-11-10) → 0.30.0

- [x] Coverage ledger: every game class with combat hooks reviewed from its hook bodies (`Diagnostics/CoverageLedger.cs`, `scripts/coverage-ledger.mjs`), compared with the running game at load; unreviewed, new and changed classes reported on encounter. Card code checked against the per-card tables (`CardCoverage`). Settings checked at load (`CatalogCheck`).
- [x] Every-power check after each play (powers that went down with nothing to explain it).
- [x] Simulator: Concoct, Monarch's Gaze (single-target and all-enemy attacks), Storm and Trash to Treasure channels.
- [x] Dead code removed (analyzers + `scripts/dead-code.mjs`): Intent Forecast stub, `UiKit.Badge`, `Sts2State.HasVar`, `TurnEndModel.Overblock`, `PlayFacts.DebuffsLanding`, `SimAdapter.SelfPowers`, unused parameters and usings.
- [x] 1.20.11: 94 unused usings removed (IDE0005, which the normal build doesn't check); `scripts/dead-code.mjs` now lists them too (`--fix-usings` removes them). `TurnEndModel.Incomplete` removed (replaced by ProgressMark in 1.17.0).
- Known gaps that could **underestimate damage to you**: none known. ~~Multiplayer Covered/Intercept~~ (0.37.4); ~~Queen and Kin Priest switching moves mid-turn~~ (reviewed for v0.113.1: neither switch changes an attack).
<!-- gaps:start (written by scripts/coverage-ledger.mjs from the review files; don't edit by hand) -->
- Known gaps (4; each is logged when present in a fight):
  - [ ] BiiigHug (model's effect): a Soot shuffled into your draw pile at an unknown spot: later draws in the same simulation don't see it
  - [ ] PersonalHivePower (model's effect): its Dazed into your draw pile when you attack it (counted on its tab): later draws in the same simulation don't see them
  - [ ] SurprisePower (model's effect): the gremlins it calls when it dies: a later hit in the same play can't target them (the next frame reads them)
  - [ ] UnceasingTop (model's effect): a draw when your hand empties during your turn: not simulated (your hand size, and what the draw sets off)
<!-- gaps:end -->

---

## 15. Out-of-combat reminders (Reward Tags, Event Reminders, Shop Reminders, Map Reminders: `reward-tags`, `event-reminders`, `shop-reminders`, `map-reminders`; Run Reminders until decision #185; goal 3 in ROADMAP)

An indicator on a choice outside combat when one of your relics changes what it does. Same language as on-card tabs (the relic's icon, crossed when it's against you).

- [x] 0.36.0: card reward panel (Bing Bong, Book of Five Rings, Lucky Fysh, Pael's Wing) and event option icons (cards, curses, HP loss, gold, potions, resting), read from each option's code; the event-option check; `docs/RELICS.md` generated with every relic.
- [x] Coverage: `Diagnostics/ChoiceHooks.txt`, every relic overriding one has a rule or a reviewed reason (`RelicChoiceTracking`), checked by the script and in game.
- [x] 0.36.1: checks on what each event option did (deck, gold, HP, potions, relics that always react) and on the card reward panel after a pick.
- [ ] Play test: event icon placement (left of the option) on every event layout, ancients included. ~~The card reward panel's default spot.~~ (The panel is gone: decision #82.)
- [x] Shops (0.40.0): Maw Bank (crossed on items: buying turns off its gold), Bing Bong / Lucky Fysh / Book of Five Rings on shop cards, Membership Card and The Courier are already in the prices.
- [x] Settings (0.43.0, decision #43): the Relics page has an Outside fights section, with a switch per place (card rewards, events, shops) for each reminder relic.
- [ ] Card removal (shop, events): no relic reacts today (`BeforeCardRemoved` has no relic); recheck each game update.
- [ ] Ancient options: relics whose `AfterObtained` adds cards or gold are covered through the relic's code; check what other ancient pickups do in play.
- [x] 1.11.0 (decision #65): pathing reminders beside the map legend, with an IL scan of room tests (`CoverageWatch.RoomTestingHooks`, `ChoiceReminders.MapReviewed`).
- [x] 1.11.2: shop price icons left of the game's coin (they covered it).
- [x] 1.11.3 (decision #67): card reward panel centered above the offered cards, rows side by side.
- [x] 1.11.4 (decision #68): About page (version, author, links, philosophy, paged patch notes from the built-in CHANGELOG.md).
- [x] 1.11.5: Impatience's and Fetch's conditional draws (the card scan now flags any drawing card that glows gold without a condition check), Fisticuffs' block through Dexterity and Frail, block and damage modifiers' flashes (Fasten) aren't missing tabs.
- [x] 1.11.6: simulation cache keyed on the card's own numbers (upgrades in hand), Echoing Slash's repeats, Expose's block and Artifact removal, every enemy's poison before any moves (Ravenous), Endless Conveyor dishes read through the event's held delegate. Reading the game's code goes through one lock (Diagnostics/GameCode): parallel reads failed silently. The card scan flags cards that take block away or react to their own kills.
- [x] 1.11.7: Outbreak's poison trigger (TriggerPoisonAction), poison trigger count from every player's Accelerant (uncapped, so new stacks tick fully), Accelerant played mid-turn in the simulator. The card scan flags cards that set off poison.
- [x] Card scan in the catalog tests (decision #70): CardScanTests runs Diagnostics/CardCoverage on every card of the installed game.
- [x] Multiplayer: teammates' powers and relics your card sets off get tabs (1.13.0, decision #75).
- [ ] Test in game (co-op): a teammate's tab on your card (Rally with a teammate's Juggernaut), and no false tab on yours for their firing.
- [x] 1.14.9: cards that act on every player: Huddle Up's draws and Blade Symphony's Shivs are each teammate's own card events (their draw and created-card procs), Glimpse Beyond's Souls are created by you (yours), Constellation's draws are its target's; Legion of Bone reviewed (nothing reacts to a summon). Alone, Huddle Up no longer draws for you and Blade Symphony no longer creates a card. Rally simulated in 1.12.2; One for All, Yoga and Plot reviewed. No card that acts on every player is left unmodelled (CardScanTests).
- [x] 1.14.10: relics and powers reviewed for teammates (decision #80): a teammate's Wuppy, their Howl from Beyond and I Am Invincible at end of turn (every player's auto-play phase, in player order), Withering Presence read only for you (one per player), Inferno and teammates' Impaled notes corrected. SimModelOwnerScanTests flags any model line that names your player.
- [x] 1.14.11 (decision #81): builds below the report floor in `reporting.json` stop sending to Sentry; the mirror reads the same file.
- [x] 1.15.0 (decision #82): card reward relics (Bing Bong, Lucky Fysh, Book of Five Rings) are tabs on the offered cards; the card reward panel is gone.
- [ ] Test in game: the card reward tabs (Bing Bong, Lucky Fysh, Book of Five Rings on the healing pick) beside Strike Dummy's.
- [ ] Test in game (1.15.1): shop icons clear of the coin with their backdrops, Lucky Fysh's net price centered under the price; map legend icons level with the legend's own.
- [x] 1.15.2: the Inspect button's icon (the game's cursor_inspect.png is imported as an image, not a texture: `GameIcons.LoadTexture` makes one); a missing icon is reported.
- [ ] Test in game (1.15.2): the Inspect button right of Colorful Philosophers' options opens the Inspect screen.
- [x] 1.15.3: Membership Card and The Courier beside Merchant on the map; shop hooks (prices, stock, shop cards, purchases) count as map hooks in the pathing scan.
- [x] 1.16.0 (decision #83): Book of Five Rings on every card reward card, the heart marker on the healing pick, crossed when part of the heal is wasted.
- [ ] Test in game (1.16.0): the heart marker's placement on the Book of Five Rings tab, plain and crossed.
- [x] A teammate's Simpleton (with their hand, for the audits and Count teammates' hands): needs any card's play built as theirs (SimAdapter.Play with an owner; only Howl from Beyond is today). (2026-11-22: their Stampede and Simpleton pick from their hand, each card built as theirs: `SimAdapter.AutoPlays` with them as the owner; the forecast check holds those turns to it.)
- [x] 1.11.8: decisions reconciled with the goals (#72 intents, #73 chances, #74 option delegates, #75 multiplayer as one team; superseded decisions struck through); README and ROADMAP refreshed; card lists by nameof.
- [ ] Test in game: About page (links open the browser, paging, search, long notes wrap); ~~card reward panel above the cards (clear of the banner and the offered-card tabs, 3 and 4 cards with Lasting Candy)~~ (gone: decision #82); shop price icons clear of the coin (Book of Five Rings on a card); map legend icons (placement left of the legend, hover tooltip and room highlight, hidden with the legend), Dowsing and Byrdonis Egg as tiny cards, the regrouped settings pages.
- [ ] Event options whose effect runs through a delegate the reader doesn't follow (a lambda passed to a card choice): the check reports a relic that reacts without its icon.
- [x] 0.31.0: everything-snapshot per play (`PlaySnapshot`, `CodeIndex`), relic tracking review for all 163 fight-affecting relics (`RelicTracking`), no blanket skips for draws and auto-plays.
- [x] 0.31.1: The Boot in the simulator (was a known gap; its hits would have caused after-play HP mismatches).
- [x] 1.27.0: The Boot's tab (a play with a hit it raises past block to its minimum), checked against its flash.
- [x] 0.31.2: failures inside the audits and coverage check are reported (they were log-only).

---

## 16. Open concerns (2026-11-10, waiting on play testing)

- [x] **Copies of a counter power, each with its own count** (2026-11-21): Orbit, Automation, Cacophony and Panache are instanced (each card played makes its own copy, counting on its own). Each copy is read into `SimPlayer.Counters` and counted on its own by its model (`CounterPower`), resetting after it triggers; a copy applied during a play starts its own count.
- [x] **Cards taken from the draw pile leave the simulator's pile as it is** (2026-11-21; fixed 2026-11-22: `TakeToHandAction`, from the draw or the discard pile, a branch per profile when the pick is a roll, `?` when your pick among them changes what the simulation follows; `SimAdapter.HandTakes`, held to `PlayFacts.TakenToHandCards` by the card scan): Wish, Secret Weapon, Secret Technique, Seeker Strike, Locked In, Anointed and Ki Chant put a card from your draw pile into your hand, counted in your hand (`PlayFacts.TakenToHand`), but the simulator's draw pile (decision #90) still holds it, so a later draw in the same simulation could draw it again. Which card is your choice or a roll: the pile should lose one card of unknown kind.
- [ ] **A card Feral sends back makes Stampede's pick unknown** (2026-11-21): `SimEngine.AddToHand` marks the hand's contents unknown for any card joining it, but the card Feral returns is known (the one just played). Keep it in `AutoHand` instead.

Recorded before the first runs with 0.26–0.31; revisit with real reports.

- [x] **Quick plays: closed by the lead-in (0.31.23, decision #27).** Was: quick plays aren't audited. A card played before the previous play finished resolving had its tabs computed without that play's effects (`ProcAudit.StaleBecause`), so its mismatches only go to the log as `[model]` lines. Now that plays are attributed to their `PlayCardAction`, look at recomputing the prediction at the moment its action starts (or comparing against the state the previous action left) so these get checked like any other play.
- [x] **Untested in game:** action-based audit attribution (choice pauses, queued plays, potions mid-turn, Catastrophe auto-plays), the shuffle prefix, the coverage and settings checks at load, the before/after snapshot check, the parallel card simulations, and the tabs moved onto the simulator (kills, Juggernaut, stuns, Curl Up, Sneaky, Enrage, Hand Drill). Expect the first reports to be a mix of real gaps and checks that need tuning; treat them as a to-do list. (2026-11-21: exercised by every play since, thousands of checked plays and the reports they raised.)
- [ ] **Review accuracy:** ~400 reviewed entries (coverage ledger, relic tracking) were written in one session from the hook bodies. Some "covered" notes rest on understanding of the mod's rules rather than a test; coverage reports and the snapshot check should expose any that are wrong.
- [x] ~~**Simulator wait cap (DECISIONS #9):** if a card's simulations hit the 250 ms cap, its simulator tabs (warnings included) are missing for a frame or two. Watch for `SimPending` notes in the logs.~~ Gone: the frame waits 8 ms at most and what's shown stays until the new result is in (decisions #104, #105).
- [x] **Known gaps that could underestimate damage to you:** Queen and Kin Priest switching to a stronger move mid-turn when an ally dies. ~~Multiplayer Covered/Intercept~~ (0.37.4: playing Intercept moves the attacks on its target onto you in its forecast). (reviewed for v0.113.1: neither switch changes an attack.)

---

## 20. Replays (decision #160, 2026-11-21)

- [x] 1.27.0: a replay has what the card's earlier plays gave it, read from its code (PlayGrowth: Rampage, The Ball, Claw, Maul; Goopy, Momentum), checked against every card and enchantment of the installed game (catalog `PlayGrowthTests`).
- [ ] Thrash's replay (its damage grows by the exhausted card's): not modelled, `?`.
- [x] 2.0.0 (decision #189): a replay is the game's play loop. Each play takes what it finds in your hand (Fiend Fire replayed exhausts the hand each play finds and hits once per card, Dark Embrace's draws included; Acrobatics, Survivor, Prepared, Dagger Throw, Hidden Daggers, Storm of Steel, Calculated Gamble, Stoke discard or exhaust on every play; Second Wind, Compact, Primal Force, The Lovers, The Moon, Crash Landing find nothing left on the second), The Hermit with a Curse and another card takes one card (exhausted or discarded is your pick: neither set off), an Exhaust card exhausts itself once, a dupe never. Put-backs (Glimmer, Photon Cut, Thinking Ahead) come after the draws, and Ki Chant shuffles before it takes when the pile is short. `PerPlayRuleTests` holds the line.
- [x] 2.0.0 (decision #190): a step on a condition is its ordinary action under a simulator condition, asked on each play as it runs: Osty after Sacrifice or Bone Shards (no block, no attack, no kill on a later play), Impatience's and Restlessness's hand, FTL's plays this turn, Fetch's first play, Ki Hop's, Bow Smack's and Wrest's arrows (at the target aimed at, no longer the worst target's), Wheel of Fortune's hand, Sunder's and Knockout Blow's kill, Timber's attack on every enemy (a whole attack, with its hooks). Double Energy's energy was already read as each play runs (GainEnergyAsHeldAction).
- [x] 2.0.0 (decision #191): calculated values on the fight's log follow the ledger (Murder, Death March, Gold Axe, Supermassive, Anguish damage; Finisher, Lunar Blast, Tear Asunder, Pull From Below hits). Not yet: Rattle's hits (Osty's attack path doesn't take them), Voltaic's channels, block from a calculated value.
- [x] 2.0.0 (decision #192): a card play's shape is decided once before its plays: Feral returns a replayed 0-cost Attack once, a new Feral counts the 0-cost Attacks started this turn, more Feral adds charges; Echo Form decides each simulated card play's count on the ledger (an auto-play after your card isn't doubled); Nostalgia fires once per card play. The other play-count sources (Burst, Duplication, One-Two Punch, Signal Boost, Tag Team, Ki Verse, Throwing Axe) and result locations (Corruption, Rebound) are models on the same hooks too (2026-11-23, round four), each from its own code. Open: Tag Team's copies per applier are one entry (the last applier read).
- [x] 2.0.0 (decision #191): the history ledger. Every question a card, relic or power asks of the game's log of the fight is read from its code (both branches, `HistoryLedgerTests`), seeded once from the game's log with the start state, and counted forward by the simulator's own events; this turn's counts start over as the enemies' turn starts. Forgotten Ritual's exhaust this turn is its condition on the ledger (`CardEnergy.Rules` no longer names it); Ki Rest's melodies and Arrow Stab's arrows are simulator conditions (`ConditionGapKinds` "this turn's history" closed). Your exhaust pile (Pact's End), the target's powers (Bubble Bubble, Molten Fist, which applies the target's own amount as it lands) and its intent to attack (Go for the Eyes) are simulator conditions too.
- [ ] Replay: tests with no simulator condition yet, decided on the board before the first play (the card scan lists each: `CardCoverage`): Memento Mori's enemies, Judgement's and Expose's (their actions ask them already: ExecuteAction, StripBlockAction). Spite's hits (HP lost this turn), Dismantle's and Wah Blast's are an attack per way of their hit count, each on its condition (decision #191).
- [ ] Draw profiles split by a condition's filter while a card that asks it is in your hand (Impatience's Attacks, decision #190), as by a take's (decision #189): more branches on draws then, a single play included. Worth splitting only when a later play asks it.
- [ ] Replay: Flak Cannon's hits are its CalculatedHits value (the statuses it will exhaust, a calculated var the reader doesn't follow), so a later play hits as the first did; read the var's lambda, then `SimCount.CardsTaken`.
- [ ] Replay: a teammate's draws (Huddle Up) and their piles aren't read into the simulator; their count is the board's for every play.
- [ ] Replay: Reboot's later plays put the hand the board had into your draw pile (ShuffleDrawPileAction's cards are the board's).
- [ ] Ki Chant: the cards it looks at and discards stay in the simulator's draw pile (only the one it takes leaves); and a made card into a full hand isn't put into the simulator's discard pile (its profile isn't built).
- [ ] The play check's orbs channeled on a replay: the simulator's channels aren't in the play log yet (counted skip).
- [ ] Hand room outside the engine's rule (reviewed in `PerPlayRuleTests`): PlayChain (the facts' draws: the play check, the Shuffle Tag, the No Draw tag) and HandDelta's cards returned by hooks after the play; read both from the simulation.
- [x] A Vigorous card's replay (the enchantment turns itself off after the first play): its bonus on the first play only, read from its code.
- [ ] Test in game: Incoming Damage over a discovery mid-fight (it shouldn't cover the three cards).
- [x] Personal Hive's crossed tab counts its Dazed: its amount per powered attack hit (decision #161), held to the cards created by the play check.
- [ ] Test in game: Personal Hive's count on a multi-hit attack (Entomancer).
- [x] Red Skull's Strength mid-play (2026-11-22, a dev review report: Hemokinesis at 42/80 hit for 28, predicted 24): `RedSkull.AfterDamageReceived` gives it as HP lost reaches the threshold, once (`StrengthApplied`).
- [x] No Draw stopping a card's draws gets its tab (2026-11-22, the owner's request, decision #171); the checks expect No Draw to flash on those plays, and a fight that ends mid-play makes fewer of Personal Hive's Dazed than its tab counted.

## 19. Game v0.113.1 (beta, 2026-11-14)

Decompiled and diffed against v0.113.0. Rule changes: Stretch now counts a card whose enchantment gains block (EnchantmentModel.GainsBlock, set by Adroit), so an Adroit card gets and spends Stretch; Show Off's upgrade no longer touches Aim (no effect). The rest is hover tips, visuals, the compendium and cloud saves (`.pre-repair` files stay local; run history counts only `.run` files), none of which the mods read.

- [x] 0.36.2: Adroit's block gets Stretch on any card (simulator, Pael's Legion and Vambrace tabs), Adroit cards get the Stretch tab and spend it in the play check, and a replayed Adroit card's later plays gain without the Stretch, Pael's Legion or Vambrace the first play spent. Reverses 0.35.3.
- [x] 0.37.1 (first multiplayer Blitz reports): Thunder was handed to the simulator twice (the proc rule's random hit on top of `SimEngine.Evoke`'s own; adapter, not simulator: the engine's branches matched the game); the forecast was recomputed between the two end-of-turn phases in multiplayer (`PlayerTurnPhase.End` with neither phase flag set) and frozen after orb passives and end-of-turn block had run (adapter timing; the simulator was right from the state it got); Run Reminders follows a game mode's static reward code (Blitz's Neow option); the play check reads `IsAboutToBlow` on the Waterfall Giant only; Energy Surge reviewed.
- [x] 0.37.2: Ice Lance hits once (its Repeat is its Frost channels; read as 3 hits, which predicted kills and hid its Thunder tab); new card scan check for an attack with a Repeat value and no hit count in its code; the shuffle check allows for a play that ends the fight.
- [x] 0.37.3 (#278): **cards aimed at a teammate** (`AnyAlly`) are simulated once per teammate, like an attack aimed at any enemy; their block (Lift, Constellation, Demonic Shield) and Ignition's Plasma go to that teammate (`SimPlay.AllyTarget`), never to you; Mimic's block is worked out on the target; Constellation's draws are theirs; the play check compares the teammate's orbs; every ally card is reviewed in `CardCoverage.TeammateTargetModelled`. Intercept followed in 0.37.4.
- [x] 0.37.3: a teammate's Imitation Learning aimed at you puts a plain tab on your Power cards (it had a crossed rule for an enemy that never has it, and teammates' powers weren't read).

---

## 18. Game v0.113.0 (beta, 2026-11-12)

Decompiled and diffed against v0.111.0 (`~/git/sts2-decompiled`, one commit per version). New: a character (the Fool: arrows that impale enemies, melodies Wah/Cho/Ki, tarot cards), the Dread run mode (12 boss Dominions), Blitz merchants, 17 relics, 41 powers, 121 cards, 3 potions, 5 events. 744 existing classes changed; the ones the mod's rules read were reviewed from their diffs.

- [x] 0.31.29: hit counts read from the card's HitCount (11 cards renamed Repeat to HitCount, 9 moved a fixed 2 into it); Thumbscrew's extra hit through Hook.ModifyAttackHitCount.
- [x] 0.31.29: coverage ledger and relic tracking reviewed for every new class; new hooks AfterArrowShot/Impaled/Dislodged, ShouldTakeExtraTurn(Early), AfterTakingExtraTurn and ModifyPreBlockDamageTarget added to the checked hooks.
- [x] Reviewed, no change needed: Thorns no longer triggers on Omnislice (the mod never modelled that exception); Juggling copies the third attack after it resolves; Brilliant Scarf counts 6 (read from the relic); Expertise retains the whole hand; Buffer ignores Brightest Flame's max HP loss; Imitation Learning only spends a charge on a playable copy; Swift and Sown act on every play of the card.
- [x] 0.31.30: the Fool's predictions: no damage from a shot with no arrow, Nail only on Impaled enemies, Wah Blast's second hit, Cho Limbs / Cho Firm double block, arrow damage with Aim (the card's damage props); melody tabs (Violent Chord, Song Path, Lullaby, Motif, Lost Lute, Conductor's Baton), arrow tabs (Explosive Arrows, Twine, Yah), Ki Verse double-play; Obsidian Vice in the simulator; Fool and tarot draws; scan checks for shots without the Shoot tag and cards that knock arrows loose.
- [x] 0.31.31 (first Fool play test, on 0.31.29): the play check knows a shot Impales and spends Aim/Quietus, hits knock Impaled off, block cards spend Stretch; created, exhausted and discarded cards of the Fool's, tarot and event cards; Ki Chant's conditional shuffle; scan checks for cards that turn into another card after the play and for shuffles only when the draw pile is short. The rest of that test's mismatches (shots with no arrow, Aim, Joy) were fixed in 0.31.30.
- [x] 0.31.32 (second Fool play test): block gains counted in full (Cho Limbs, Cho Firm, Second Wind), temporary Thorns (Cho Barbs), Serrated Arrows / Wah Hammer losing their bonus once the last arrow is out (HitDamageLater), Rain Shot's spread (SplashFromTarget), Vital Spark's Tainted on your incoming hits, Galvanic's damage to you after the play (DamageToYou), Asleep waking stunned on any damage through block; all-enemy procs after a single-target card reach every enemy; play-check fixes for Free Card, Mummified Hand, target-dependent draws (Ki Hop, Wrest), procs at the fight's end and Hardened Shell caps.
- [x] 0.31.33 (third Fool play test): Wuppy in the simulator (yours), Stretch on a replay's first play only, Nail's and arrowless shots' debuffs, play-check fixes for Vigor, Gigantification and Tender.
- [x] 0.31.34: forecast check counts an enemy your Thorns kills mid-attack as having acted; card scan fixed for the game's template cards (Wrest, Cho Firm, Ki Chant, Florescence), target-dependent draws (Ki Hop, Wrest) and exhausts by kind (Florescence, Growth); relic check ignores model-copy methods (Yummy Cookie).
- [x] 0.31.36: a replayed single-target card that makes its target Vulnerable (Break) hits harder on its later plays (HitDamageLater); the play check reads Razor Tooth from the played card's upgrade (the relic never flashes).
- [x] **Replays and spent bonuses:** Vigor and Gigantification boost one attack; a replayed attack's later plays don't get them, fixed in 0.34.0 (Wah Touch ×2 with 8 Vigor: 12, then 6; Vigor with Gigantification lasting past the first play isn't modelled), like Stretch for block (fixed in 0.31.33). The other way too, fixed in 0.31.36: a replayed single-target card's later plays get the Vulnerable its first play applied (Break replayed on the Queen: 15, then 23). Impaled the first play applies, fixed in 0.31.40 (Rapid Shot ×4 with Serrated Arrows hits 2, then 3).
- [x] 0.31.37: tabs greyed out like a disabled enchantment (the copied tab kept the game's disabled tint on its label and backdrop).
- [x] 0.31.37 (Fool play test 4): Sown's energy, Cho Ki keeping Stretch on replays, Surrounded facing, The Moon, The Magician, Wheel of Fortune, Ki Chant replays, Wrest's draws, Stock spawns, The Fool's random plays, extra turns in the forecast check; report tags.
- [x] 0.31.38 (Defect play test): Swift's draws, evokes of orbs channeled in the same play (Voltaic), Plasma energy and end-of-turn-anyway enemy powers in the play check; Death Skull spacing.
- [x] 0.31.39 (Fool play test 5): Repeat Shot's X hits (scan check for X-cost attacks), Slumber in the simulator, Adroit's block, the code scans read local functions (Blossom Shot's Stretch).
- [x] 0.31.40: Motif's draws (Shuffle Warning, draw procs), Ki Chant replays shuffling on a later play, replayed shots impaling for Serrated Arrows' bonus; the forecast check counts the overkill of a hit that kills you (Fairy in a Bottle).
- [x] ~~**Forecast, unexplained:** Kaiser Crab, Crusher alone after Rocket died: Thrash predicted 22, hit for 18.~~ (0.31.40: the hit killed you, 18 HP left, and Fairy in a Bottle revived you; the check now counts the overkill.)
- [x] 1.2.3 (#336, simulator): Kaiser Crab, Crusher killed by Repeat Shot: Rocket's intent predicted 16, game 11. SurroundedPower.AfterDeath turns you to face the crab left; the simulator only turned you on a targeted play.
- [x] 1.2.4 (#331, draw rule): Ki Hop+ with Glam at an enemy with one arrow, 1 hit left: predicted 6 draws, game 3. The first play's hit loosened the arrow, so the replay wasn't Impaled (KiHop.OnPlay checks wasImpaled per play); Wrest's replay draws nothing either. Draws are counted per play from the arrow counter (SimEngine.PlaysStartingImpaled); the audit takes them as the play starts.
- [x] 1.2.4 (#337, audit): Cho Ki "didn't fire: StretchPower": Cho Ki spends Stretch and then applies the same amount, so before and after were equal. The spent-power check reads the decrease from the history.
- [x] 1.3.0 (settings review): Synthesis's Free Power had no tab (ledger said ignore); free-card `?` never rendered; free-card amounts told Frantic Escape copies apart (decision #54); aim marker for target-dependent tabs (decision #55); menu wording for Indirect procs, Mighty Wings, Envenom, Concoct, Conqueror.
- [ ] Test in game: the aim marker's look (size, corner, arrow direction) on a lethal skull and a crossed tab; a free-card `?` on Snecko Eye and Frantic Escape.
- [x] 1.4.0: Guide page in the settings menu (decision #56).
- [ ] Test in game: the Guide page (cards placed with room for their tabs, tabs drawn, flashing rows, the sums).
- [x] 1.5.0: settings gear beside the version note on the main menu.
- [x] 1.5.1: larger aim marker (60% of the tab icon, grown up and to the left from its bottom-right).
- [x] 0.33.0: a Stretch tab on block cards that spend it (the play check compares it with your Stretch after the play; a scan check flags block cards whose block value it can't read).
- [x] 0.33.1: no double-play or replay tabs on unplayable cards (Throwing Axe on a curse).
- [x] 0.33.2: the play check accepts a double-play source firing (Throwing Axe) when the card played more than once.
- [x] 0.34.0 (reports #217–#226): The Hermit discards before it draws (hand limit) and exhausts a Curse (scan check for cards that discard and draw), Nostalgia tab, The Magician's slots before its channels, Judgement in the simulator (scan check for cards that set HP), Unsettling Lamp with temporary debuffs and its doubling in the predictions, Vigor and Gigantification on replays.
- [x] 0.34.1: slow-frame log (the mod's time per feature over 30 ms; game stalls over 100 ms with the mod's share). Asked for after a stutter on a lethal Wah Blast that the log couldn't place.
- [x] 0.34.2 (reports #227–#231, #128 again): Slippery as a damage cap in the play check (#128's first cause, Hardened Shell, stays fixed), random auto-plays' block and damage, played cards' random cost cleared, Punch of Joy's transform; the mod's own slow frames reported (over 50 ms, from a feature's second), the game's logged only.
- [x] 0.35.0: Vigor and Gigantification tabs on attacks that spend them (Akabeko had no tab), checked against the amount after the play like Stretch.
- [x] 0.35.1 (reports #232–#239): Timber in the simulator (scan check for a second damage value), Pael's Legion and Vambrace need block above 0 (No Block) and double only a replay's first play, Burst and Duplication used by their replay, card costs compared without relic and power cost effects (Brilliant Scarf), discards shuffled back.
- [x] 0.35.2: Pael's Legion and Vambrace tabs off cards without a block value when nothing lands (Fisticuffs under No Block, Entrench with no block).
- [x] 0.35.3 (report #240): Adroit on an attack doesn't get Stretch (StretchPower.BeforeCardPlayed only registers block cards).
- [x] 0.35.4: Pael's Legion and Vambrace tabs count Adroit's block (an Adroit attack uses them up). ~~Not modelled: a replayed Adroit card's later plays still predicted with the doubled block.~~ (0.36.2)
- [x] 0.35.5 (report #241): Swift draws after the card's play, behind the cards it put in your hand. Which cards create into a pile instead of your hand is a list (PlayFacts.GeneratedToPileCards, from the decompile); the code scan can't read the pile, so a new one shows up only through the draw check.
- [x] 0.35.6: Capture Spirit's unblockable damage in the simulator (Slippery tab, kills), with a scan check for non-attack cards that damage an enemy. Found in game.
- [x] 0.35.7 (report #242): Yah tab from the simulator's kills: a proc (Violent Chord) killing an enemy your arrows Impale dislodges them all, or a shot's target it just impaled. Not modelled: Twine's block count still uses only the card's own kills.
- [x] 0.35.8 (reports #243–#260, Necrobinder): Osty's attacks in the simulator (Osty as the dealer; Thorns hits Osty; no Envenom or The Boot), Necro Mastery on Osty's death (tab and simulator), Slow on replays (the second play's damage for every later play), the Waterfall Giant about to blow, relic tabs on a fight-ending play. Capture Spirit (#254, #257) was fixed in 0.35.6.
- [ ] **The Fool, still open:** ~~Timber's damage to every enemy on a kill~~ (0.35.1); ~~Blossom Shot's Stretch~~ (2026-11-22: `AttackAction.StretchFromDamage`); ~~Epic Retrieval's energy~~ (1.21.11, `CardEnergy`); ~~Motif's draws for the shuffle warning~~ (0.31.40); ~~what Lost Lute plays~~ (2026-11-22: the top of your draw pile, `AutoPlayFromDrawAction`); Covering Fire and Compost (teammates).
- [x] 0.32.0: your HP in the simulator; lethal skull on Incoming Damage; Fairy in a Bottle and Lizard Tail revives (icon and HP after).
- [x] 0.37.0: a teammate's Ethereal cards exhausting at end of turn are simulated (Feel No Pain, Charon's Ashes, Forgotten Soul; parity cases); setting **Count teammates' hands** (off by default, decision #38) shows predictions with their hand; the enemy-turn check always uses it. Not counted even with it on: Stampede (a random card).
- [x] **Teammates' revives:** a teammate's Lizard Tail (their potions aren't known) isn't modelled. (2026-11-22: already modelled, decision #93: `FillSurvival` reads every player's Fairy in a Bottle and Lizard Tail, and `SimEngine.Lose` revives any player.)
- [x] ~~**Wuppy (event relic):** its pet takes the powered attacks aimed at you while it lives (WuppysLoyaltyPower.ModifyPreBlockDamageTarget). Incoming Damage and the Death Skull count them on you (overstated, never under).~~ (0.31.33)
- [x] **Extra turns** (0.40.0, decision #40): Incoming Damage shows the source's icon and +1 turn; the forecast stays the enemies' next turn.
- [x] **Enemy Explosive** (0.38.0): its death damages every player in the simulator (wherever it dies), with a crossed kill tab; Stock's replacement Axebot gets one too.
- [x] **End of turn** (0.38.0): Insatiable Dominion, Lunar Pastry with Black Hole, Pael's Eye's hand exhaust, and the auto-play phase (Stampede, Simpleton, Howl from Beyond, I Am Invincible; decision #39).
- [x] 1.16.1: Stampede's picks are aimed at each enemy (the adapter's plays carry their target on the card play), and a pick that draws still lands its hit.
- [x] 1.16.2: a play's target is kept in one place, SimPlay.TargetId (decision #84).
- [x] 1.16.2: Hex's Ethereal ends with its caster (decision #85); the forecast check runs on a turn that ends in your death.
- [x] 1.16.2: the forecast check keeps the branches whose Stampede/Simpleton picks are the game's (decision #86).
- [x] 1.16.3: every outcome explored, in slices, on worker threads, updating as it goes (decision #87, stage 1).
- [x] Decision #87 stage 2 (1.17.0): what's shown while exploring. Chances (ranges: explored deaths to explored plus unexplored) on your Death Skull, the enemies' and lethal tabs (aim marker kept); tabs only appear or narrow; an in-progress mark on Incoming Damage and the skulls ("incomplete" when capped).
- [x] 1.18.0: Exploration Progress, a clock and bar under End Turn while predictions explore (decision #89).
- [x] 1.18.1: draws come from your draw pile by profile (decision #90, stage 3 part 1).
- [x] **Decision #94 (accepted, not a gap):** with Count teammates' hands off, predictions can be off or a tab missing where a teammate's hand changes the end of turn. By design; never reported as a mismatch.
- [ ] **Decision #93: what a teammate doesn't get yet from the one engine:** their end-of-turn auto-plays (Simpleton's pick: their card plays built as theirs, decision #80) and their draw pile (`SimAdapter.FillDrawPile`, DrawPileMemory for their pile); the Death Skull on Teammate Incoming Damage (their `Died` is now tracked).
- [x] **Main branch member check (decision #162):** every game member the mods name, checked against main's v0.111.0 decompile in the catalog tests; the 9 beta-only ones it found are read by name now.
- [x] Written for beta, accommodating main (decision #163): beta-only parts behind `[BetaOnly]` gates (Explosive, Simpleton, Free Card, Bath Robes, RunModel, Holders, IsDread, the late multiplier, the extra turn's source, the arrow count, Impaled's arrows and hits left, the enchantment block flag, the run's end), gates checked off as main catches up, `nameof` for every beta-only class, and `MainReviewed` (v0.111.0). CardModel.GainsBlock and Galactic Dust's StarsSpent were read by name though main has both: plain code now.
- [x] Steam branches workflow (decision #164): a `steam-update` issue when main or beta gets a new build, with its patch notes and steps.
- [x] The v0.113.0 crash (issues #395 to #470): EnchantmentModel.GainsBlock, added in v0.113.1 so Stretch counts a card whose enchantment gains block (Adroit; modelled in 0.36.2), named on v0.113.0. The gate's fallback (no enchantment flag) is v0.113.0's own rule: there StretchPower checks only CardModel.GainsBlock. Checked against the v0.113.0 decompile: the only member the mod names that v0.113.0 lacks, and it's behind its gate now.
- [x] Each decompile keeps its build's `sts2.dll` in `assembly/` (decision #165); beta's filled from the installed v0.113.1 (its hash matches the decompile's).
- [x] Gate detection on main's real assembly (2026-11-22): with v0.111.1 installed, `EveryGateOpensOnBeta` lists all 14 gates closed, as they should be on main. When a simulator rule first depends on the game version (decision #163: detected from the game's code, never the version string), test its detection on main's `assembly/sts2.dll` the same way.
- [x] Decompile main's v0.111.1 (2026-11-22): code-identical to v0.111.0 (only the build's commit string changed); no gate came off, `MainReviewed` is v0.111.1, and the coverage check accepts main's reviewed version (`CoverageLedger.ReviewedGameVersions`).
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
- [x] (2026-11-22: `ShuffleDrawPileAction`, your hand's profiles in with it) Decision #91 gap: a card play that shuffles your draw pile mid-play (Reboot: your hand into the draw pile, then CardPileCmd.Shuffle, then draws) draws in the simulator from the pile as it was; the shuffle isn't part of its play. The known top is forgotten in game when it happens.
- [x] Decision #91 gap: cards auto-played from the top of your draw pile during your turn (Havoc, Cascade, Lost Lute, The Devil) are simulated (`AutoPlayFromDrawAction`, `SimDrawCard.FromPile`; the card scan holds every card calling `CardPileCmd.AutoPlayFromDrawPile` to `SimAdapter.PlaysFromDrawPile`). Mayhem plays at the start of your turn, after the forecast; Distilled Chaos is a potion (potions show only the relics drinking them sets off).
- [x] 1.18.2: the known top of your draw pile (decision #91), shuffles forgetting it, Perfect Fit, I Am Invincible as a chance, the draw scan.
- [x] 1.18.3: a card added to a full hand goes to the discard pile (your hand size stops at 10 in the simulator). The draw pile as a known top stack (cards placed on top, in order) and a random rest; a draw branches only over what matters about the cards (acts in hand at end of turn, Ethereal with exhaust reactions, reacts to being drawn, a later Stampede/Simpleton pick); the rest is one plain group. A scan for draw-reactive cards and powers.
- [x] 1.20.9 (decision #104): the frame waits 8 ms at most for simulations (it was 250 ms for cards, unbounded for the forecast); background snapshots every 250 ms; cap hits logged (`[sim] cap:`) and counted in the combat summary with the waits.
- [x] 1.20.13 (decision #107): one merged search per simulation (no slices, no step budget, no sample, no safe run, no snapshots); teammates in the merge key; memory and work caps with the settled floor; Incoming Damage's ? at a cap and after 100 ms waiting on a newer result.
- [ ] Decision #107 follow-up: play-test on busy boards (four Defects, multiplayer chains, multi-hit cards with orbs): how quickly tabs settle with every card's search running, whether the 100 ms waiting ? feels right, that nothing blinks. Read the `[perf] combat summary` lines and the `sim-cap` reports: whether either cap is ever hit, and the memory per state in real fights (about 2 KB measured in tests).
- [ ] Decision #107 follow-up: chains inside one step run separately on every branch (Beacon of Hope sharing block with Juggernaut, nested Then): with three or more such players a step makes millions of copies for a few hundred results, and the work cap stops it. Merge those chains across branches (advance every branch through each stage of the chain together) so they finish.
- [ ] Decision #107 follow-up: every card's search repeats the end of turn; cards whose play leaves the board in the same state could share it. Measure first.
- [x] 1.21.0 (decision #108, phase 1): the event stream; session summaries to Sentry (clean, unchecked, capped and mismatched turns; checks, searches, waits, frame times, garbage collection); the unseen-end file; the pinned session metrics issue from the Sentry sync; dev mode from the install marker; docs/DEVELOPMENT.md "Coming back after a break", "Dev mode", "Session metrics".
- [x] Decision #108 follow-up: the session metrics read (2026-11-21): 1.21.2 (multiplayer, 17 h) 83% clean turns, its forecast mismatches all from intent powers (a 1.21 build); 1.24.6 100% clean over 73 turns; no caps hit.
- [ ] Check the Sentry quota the session summaries use; and once 1.26+ has hours of play, whether its slow mod frames (275 an hour on 1.24.6, over half an hour of launches) are real.
- [x] 1.21.1 (decision #108, phase 2): the dev dashboard. In dev mode the game writes the event stream (`stream-*.jsonl`, with 250 ms frame rollups and the mod's log lines), `state.json` and reads the page's `inbox/`; `tools/dashboard/index.html` (no dependencies, File System Access API) shows Overview, Turn (with the forecast's spread over the rolls, from phase 3), Searches, Performance, Reports (in dev mode none is sent: each is kept in `reports/` with everything it carries, notes and a handled mark beside it; session summaries in `sessions.jsonl`), Feedback (`feedback.jsonl`, read at the start of each stretch of work: CLAUDE.md), Log and Compare (two launches). docs/DEVELOPMENT.md "Dev dashboard".
- [x] 1.21.2 (dev review): Pael's Tears judged once the play has resolved (a simulator-only step, `SimModel.AfterPlayResolved`), and energy from Orbit, Automation, Subroutine, Yah, Ivory Tile and Nunchaku reaches the simulator (`CardEvent.EnergyGained`); before, no model gave energy, so Pael's Tears warned on cards that got it back.
- [x] 1.21.2 (decision #109): Intent Details on moves whose powers depend on a branch: the calls on every path read from the code (`Branches`), a rule per move for the rest (`MoveBranches`: nine moves), a ? without one or on Intent Forecast's later moves, and the catalog scan (`MoveBranchTests`).
- [x] 1.21.3 (dev review): Osty attack cards from the game's tag (`PlayFacts.IsOstyAttack`, `CardTag.OstyAttack`; the name list missed Fetch) with a card-scan check for a card attacking from Osty without the tag; a negated getter amount in a move's code (Toadpole's Spike Spit: -2 Thorns); "Checking for Spire QoL updates…" on the main menu. Dashboard: a hover readout on every chart, a log scale for the time charts, comments on notes, and the launch files' list with a clean-up rule.
- [x] 1.21.3 (decision #110): Ethereal cards fade as a whole: `%CardContainer` in a CanvasGroup while faded, put back after. To check in game: hover, drag, play and the card's fly-out animation on a faded card; the event inspect screen; a reused node after the card leaves your hand. Taken back in 1.21.6 (decision #112).
- [x] 1.21.4 (dev review): Evil Eye and Death's Door count their repeated block gains (`PlayFacts.BlockGainCount`, reading the game's own WasCardExhaustedThisTurn / WasDoomAppliedThisTurn), with a card-scan check for a card whose OnPlay can gain block more than once (a loop or two calls) without a rule (`PlayFacts.RepeatedBlockCards`; Rally reviewed: one gain per player). Intent Details' check counts a power a move applies to its own monster with no applier (Devoted Sculptor's Ritual). The dev folder's state file being read by the page no longer logs a warning.
- [x] 1.21.10 (decision #116): every part of the play check left out goes through `ProcAudit.NotChecked` and is counted (random auto-plays, random kills and draws the simulation couldn't account for, target-dependent shuffles; powers during random auto-plays were skipped silently); the intent, potion, reminder and rest checks post to the stream, counted apart from the turns (`SessionSummary.Others`); the simulator's unused lead-in removed; `SimExploration` renamed `SimSearch`.
- [x] 1.21.11 (decision #117): crossed "gives up a relic's bonus" tabs are checked: Art of War, Ripple Basin and Pael's Eye by the relic going dark, Pael's Tears by the energy left after the play, Biiig Hug by its flash. Energy after each play is compared with the prediction. A card's own energy is simulated (`CardEnergy`: read from its code, rules for Sunder, Bow Smack, Restlessness, Forgotten Ritual, Double Energy, Mad Science, Epic Retrieval and the teammate cards; `CardEnergyTests` scans every card), so Pael's Tears no longer warns on a card that gives its energy back.
- [x] 1.21.12 (decision #118, dev review report 20261120-150454-32cef7): Escape Plan's block only when it draws a Skill (it was certain); Pillage draws again while it draws an Attack (it was one draw, and the play check called more "unexplained"); Scrape discards what it drew unless it costs 0. Draw profiles carry type and cost while such a card is in hand (`SimAdapter.DrawnCardReaders`, `DrawnCardAction`); `DrawScanTests` finds every card that uses what it draws.
- [x] 1.22.0 (decision #119): the draw chance tab: Escape Plan's chance of a Skill in the middle; Pillage's most likely number of Attacks and Scrape's of cards that cost 0 in the corner, with its chance in the middle (ties to the higher). Setting `drawn`, Guide examples, checked by the play check (outcome possible, odds totalled).
- [ ] Test in game: the draw chance tab's text and corner number on Escape Plan, Pillage, Scrape (hand and Guide page), and that other tabs on a reused tab node don't show a stray corner number.
- [x] 1.22.1 (decision #120): Pillage draws level by level, merging draws that gave the same cards; what was drawn is in the branch key while it draws, which fixes Pillage's chance tab with Iteration (two branches with different Attack counts merged: 75% shown for 25%). Tests against the textbook odds for every pile and hand size (Pillage, Scrape, Escape Plan), and a cost test (four kinds of Attack: 518,593 state copies before, under 5,000 now).
- [x] 1.22.1 (decision #121): Hellraiser's auto-played Strike is simulated (its play at a random enemy, mid-draw); the play check follows plays where it happened.
- [x] 1.23.0 (decision #122): settings by category: Show chances with each chance under it (Intent Forecast's random moves new), kinds of tab (plain, crossed, aim marker), reminders by place, options greyed out under their feature.
- [x] 1.24.0 (decision #123, dev review notes): options directly under their switch, Tabs on cards master, Run Reminders' places under it; Book of Five Rings' heal marker is Regen's plus; the Guide's example cards stop redrawing once settled (the Guide page allocated 6.7 MB/s, a 30 ms collection every 2.3 s).
- [x] 1.24.4 (issues #439, #440): the play check expects Legion of Bone's summon on every player (SimAdapter.AllPlayersSummonCards) and the Die For You a new Osty gets from the summon (OstyCmd.Summon).
- [x] 1.24.6 (owner's idea; dev review report 20261121-005552-a66f04): Pocketwatch's crossed tab on the play that passes its limit (model Pocketwatch, checked by its counter: ActedSeen.CounterPassedLimit); a play that ends your turn (Void Form) skips the after-play comparisons, counted (decision #126).
- [x] 1.24.6 (decisions #127 to #129): the glow left out under see-through cards; each build's commit in the dev data; per-feature memory per frame measured, and the forecast model, Shuffle Warning's facts and the frame key no longer rebuilt every frame.
- [ ] Test in game: the Ethereal glow mask (the glow rings Void Form instead of showing through; it still pulses; no shader error in the log), and the Performance page's memory column after a fight.
- [x] 1.24.6 (decision #130): the board probe: the forecast, Proc Indicators' key and Card Indicators reuse last frame's results while nothing changed.
- [x] 1.24.6 (dev review, Necrobinder): Sic 'Em's summon modelled (model SicEmPower, it was "ignore"); Gremlin Horn's energy and draw in the simulation (model GremlinHorn); the outcome audit's hits through block counted until your end of turn is over (SimEnemy.UnblockedAtTurnEnd: Necro Mastery's enemy-turn hits aren't your end of turn's). A board probe miss now names what changed (the state's differing fields, or the card and its tabs). Dashboard: line charts smoothed (5 points), a switch on the Performance page.
- [x] Gap: a teammate's Sic 'Em on the same enemy isn't told apart from yours (the adapter reads the power by type): your Osty's hit predicts a summon theirs would give their Osty. (2026-11-22: only your instance is read, `SimReaders.Powers`; a teammate's reacts to their Osty, whose hits aren't simulated in your turn.)
- [x] Board probe misses (dev review): every one was the end of the turn (hand discarded, turn counters reset, enemy debuffs ticking down) seen by a result worked out the turn before: the skip compared with last frame's probe, and the board changed on frames where the result wasn't asked for. Each result now keeps the probe it was worked out from (decision #130).
- [ ] Test in game: memory per frame in fights with the board probe (the Performance page's memory column), and no `[perf] board probe missed a change` in the log.
- [ ] Test in game: the settings menu's new sections (Chances, Kinds of tab, Reminders by place), greyed-out options when a feature is off, and Show chances off in a fight (no chance on the Death Skull, lethal tab, draw chance tab or Intent Forecast).
- [ ] Gap: Scrape discarding a Sly card plays it (not simulated: `?`).
- [x] A random orb (Chaos, The Magician, Trash to Treasure: `OrbModel.GetRandomOrb`) is a branch per orb, 1/5 each (`OrbKind.Random`); it used to be an orb with no values that did nothing, with no `?`. An orb the simulator doesn't know (another mod's) is `?` at the end of the turn.
- [x] Decision #166: card simulations start alongside the turn forecast's, not after its result.
- [x] Dev review (2026-11-22, 1.27.0 play): Minions die with the last main enemy (CreatureCmd.Kill); a move's amount that multiplies two numbers (Axebot's Boot Up); stale legend and option rows (Godot renaming a same-named node queued for deletion: `UiKit.RemoveAndFree`); no 0% event icons; outcome-rule tabs per aim in the checks (Gremlin Horn); Centennial Puzzle's draws from HP lost to something the play sets off; the dashboard counts every search, not its last 5,000.
- [x] (2026-11-22: `PlayFacts.CountSimulatedHpLoss`, from the simulation's play log) The Shuffle Warning's draw count (PlayFacts.Draws) has Centennial Puzzle's draws only for the card's own HP cost: HP lost to an enemy's reaction (Galvanic, Thorns) that sets it off isn't counted there, so a shuffle it causes isn't warned of. The simulator has it (its draw tab).
- [x] Decisions #169, #170 (2026-11-22): rules main does differently (Buffer and Brightest Flame, Omnislice's splash, Juggling, Plow, fixed hit counts) are branch rules read from the game's code, checked on both branches' assemblies; each branch's reviewed build is named in `SupportedVersions`, and the check fails on any other installed build. Reviewed with no prediction change: Intercept and Covered's cleanup, Imitation Learning's charge, Pale Blue Dot's display, Phantom Blades' Retain, Lethality's guard, Expertise's retain (Ethereal still exhausts), the death hooks' new parameter, and reordered once-a-combat flags.
- [x] Unceasing Top draws in the simulation when a resolved play leaves your hand empty (AfterPlayResolved, as `CombatManager.CheckForEmptyHand` after the card reaches its pile, with no card of yours still being played).
- [x] Surprise's gremlins join the simulation when the Gremlin Merc dies (`SimEnemy.Spawns`, `SimEngine.Spawn`: a branch per HP each can roll, from `Creature.SetUniqueMonsterHpValue` and the multiplayer scaling; they do nothing this turn).
- [x] Personal Hive's Dazed and Biiig Hug's Soot go into the simulated draw pile at a random spot (`SimEngine.IntoDrawPile`: a branch per spot among the known top cards, else into the rest; their profiles read for the board: `SimPlayer.MadeCards`).
- [x] Gap: Drum of Battle's energy when it's exhausted during another play (Exhaust a card from your hand while it's there) isn't simulated (`CardEnergy.Reviewed`). Now simulated (decision #144): certain when the play takes every card, its chance at random, unknown when you choose.
- [x] Regen (RegenPower.BeforeSideTurnEndEarly) heals you before the enemies act, up to max HP (was reviewed "ignore"). Void's energy when drawn mid-play (CardEnergy.LostWhenDrawn; was "ignore").
- [ ] Tests: the simulator files coverage shows least run (`./scripts/coverage.ps1`, 2026-11-21: 90.6% overall): `Sim/Models/BlockModels.cs` (35%: Pael's Legion, Vambrace), `Sim/Models/EnemyModels.cs` (28%), `Sim/Models/ProcRelicModels.cs` (76%). A scenario test per untested hook, from the decompile.
- [ ] Tests: mutation testing (`./scripts/mutation.ps1`) on the simulator's core files, file by file; `SimChance.cs` scored 72% (2026-11-21).
- [ ] Gap: a Sly card discarded from your hand by another card without a choice (the whole hand: Storm of Steel, Calculated Gamble, Shadow Step, Wheel of Fortune) plays itself (CardCmd.Discard, AutoPlayType.SlyDiscard): such a play is `?` (decision #146). Model it as an auto-play at a random target (the Stampede plays, played after the discards and their draws). A discard you pick (Acrobatics, Survivor, Prepared...) is a card, never a particular one (2026-11-23, the owner's rule): a Sly card playing itself then is your own decision, neither simulated nor marked, and the Play audit excuses it ("the card you picked played itself (Sly)", counted).
- [ ] Gap (the chosen half decided 2026-11-23: a pick is a card, never a particular one; what depends on which card is neither simulated nor marked): which cards a random discard or exhaust takes isn't kept, only how many (decision #146): a Burn, an Ethereal card or a card Stampede could pick is still counted in your hand after Survivor or Cinder.
- [x] Gap: a card that acts when exhausted, exhausted outside a play (Ethereal at the end of your turn, an enemy's or a power's exhaust): its energy isn't simulated (nothing after it in the turn reads your energy today). (2026-11-22: closed, nothing to predict: no prediction reads your energy after it, the end of your turn included; it would be reopened by a rule that does.)
- [x] Gap: a teammate's own Energy Surge (played through Howl from Beyond) gives you energy the simulator doesn't follow. (2026-11-22: closed, nothing to predict: it comes from their end-of-turn auto-play, after which nothing reads your energy this turn.)
- [ ] Idea (owner): a crossed tab on cards for end-of-turn relics that count your hand (Cloak Clasp's block, Obsidian Vise's damage): playing a card loses one card's worth. Every card would carry it while you hold one; decide the design first.
- [ ] Decision #108 follow-up: use the dashboard in a few real sessions; prune what nobody looks at, and add what was missed (time from a board change to every tab settled per board, the adapter's own time per frame apart from its feature).
- [x] (2026-11-22, decision #173: test in game with the dashboard's memory lines) Decision #172 follow-up: the mod's frame work allocates about 11 MB/s in a fight with nothing changing (a 2026-11-22 dev session: Proc Indicators about 16-24 KB a frame, the turn forecast 6-11, widget layout 6), about three quarters of the process's allocation, which sets off a quick collection (7-8 ms) about every 1.3 s. Find what each allocates on an unchanged board and keep it from doing so.
- [x] 2026-11-22 dev review: Leading Strike's count (2 Shivs, a `Shivs` var; the rule said 1) with a scan for it (`MadeCountScanTests`: a card whose play reads one of its card counts makes that many); a teammate's Impaled read as yours (its id made before the rename; `PowerReadTests`); the board probe hashes each power's simulator read (`SimReaders.Read`: Skittish's block this turn changed the forecast's start with the probe the same).
- [x] 2026-11-22 dev review (2): Crab Rage on an enemy with no attack intent; Imbalanced and Parrying Shield not held to the play's flashes (`OutcomeRule.InPlay`); a fired model counted as lasting in a warning's chance (`SimOutcome.LoggedChance`: Personal Hive's 0%? on Neow's Fury); idle garbage in the Shuffle Warning, card tabs, Relic Counters, Intent Forecast and the shop's card tabs.
- [x] 2026-11-22 dev review (3): quiet-board garbage from the first sampled session: an Ethereal card's fade (its "anything new?" check built every node's text every few frames: about 5.6 KB a frame per faded card), the play check re-subscribing every frame, Potion Tabs copying the fight's history every frame, the coverage watch, creature-node lookups; the sampler's own garbage (bursts, its thread left out).
- [x] 2026-11-22 dev review (4): quiet garbage in fights down to 1.9 KB a frame (from 81 before decision #173): the tab filter's group key built per tab per frame (`QolSettings.GroupOf`), the Ethereal fade looking for changes every third frame (now on a board change or every 15th), Rest Site Preview working out the heal every frame at the fire; the sampler left out only its first delivery thread.
- [x] Decision #176 follow-up: the first sampled sessions' quiet garbage cut (2026-11-22); the quiet moments' numbers turned out to be mostly dev mode's reuse checks.
- [x] 2026-11-22 dev review (4): steady frames (decision #177): every feature that allocates while nothing it reads changes, however little, with how often; dev checks counted on their own; the Ethereal fade looks only on a change (board, aimed target, pile).
- [x] Decision #177 follow-up: the first steady session's table (2026-11-22): the map legend (its 6-frame poll), the Ethereal glow's names, Intent Forecast while hovered, Rest Site Preview away from the fire and its widgets (the run's player looked up four times a frame), Potion Tabs before the first fight, the card tabs, Proc Indicators, Shuffle Warning, Incoming Damage, Teammate Incoming Damage, the forecast's reused frame, the session's place (dev mode); and the scans that hold the line (decision #178).
- [x] The dev stream pauses its frame data while the game's window is in the background; the charts show the time away as a shaded gap (decision #179).
- [x] 2026-11-22 dev review (Ironclad win): Vicious and Dark Embrace draw their amount; a play's cards leave the hand before their events, and before the attack when its code takes them first (Fiend Fire: CardCode.TakesBeforeAttacking); No Draw and Fiddle in the simulator; Self-Forming Clay and empty-pile draws in the checks (decision #181); a win counted as won; the second steady session's leftovers (decision #180). The shop hitch on buying a card was the game's own (its frame rate dropped for a second with the mod's work under half a millisecond and none of its hooks running).
- [x] Decision #177, second session: fights' steady garbage down to about 19 KB/s; out of fights, Run Reminders and Rest Site Preview on change (decision #180).
- [ ] Decision #177, third session: read the steady table again. Rest Site Preview at the fire (about 1.8 KB a frame) is timed in parts now (rest-preview:read, :hover, :key). Keyword Tags and Ethereal Fade (small, now and then; one feature, Card Indicators, until decision #185). The settings menu's Guide and Setup pages (about 14 KB a frame while open). The Battleworn Dummy fight's Fiend Fire predicted no Charon's Ashes damage: retest with the exhaust-first order.
- [ ] No Draw and Fiddle at the end of your turn (Dark Embrace's saved Ethereal draws): the simulator stops draws only inside a play.
- [x] Card programs, first step (decision #182): a card's actions in its code's order, read from the running game (beta and main), with behaviour tests (PlayBehaviourTests) and program tests on both branches. Iron Wave, Dash, Wah Cho, Wah Cho Ki, Second Wind and 10 more now in their own order.
- [x] Card programs: eight value tables read from the program's commands and whom they act on instead (`CardEffects`: Osty killed, a target's HP set, a target's block taken, damage without an attack, block for another or for each player, a channel for the target's player, a second attack on every enemy after a kill). The reader follows static helpers of the game's models (Blade Symphony's `Shiv.CreateInHand`) and names each argument's owner (`Owner.Creature`, `cardPlay.Target`).
- [x] Pipeline steps (decision #182, part B1): each simulated command's hooks in order (`Sim/SimPipelines.cs`), compared with the game's on both branches (`PipelineParityTests`); four differences modelled, the rest reviewed.
- [ ] Pipeline gaps found by the check: a player's Intangible flash on an enemy hit (AfterModifyingDamageAmount); Covered and Guarded ending when the teammate who gave them dies (AfterDeath); a player's death and Fairy/Lizard Tail settled after AfterDamageGiven/Received, with AfterDamageReceived skipped on a killing hit (the game's order); a discarded Sly card's auto-play (ShouldPlay, BeforeCardAutoPlayed, already `?`).
- [ ] Pipeline follow-ups: declare the other damage-to-a-player paths (`LandBlockable`, `LandUnblockable`) as pipelines too; AfterDamageReceived on a killing hit on an enemy (the game skips it); Snecko Skull with Unsettling Lamp (additive before multiplicative in ModifyPowerAmountGiven); enemies' attacks running BeforeAttack/AfterAttack (Painful Stabs, Skittish, Suck).
- [x] Model amounts (decision #182, part B2): `ModelAmountCheck` compares where each relic and power model takes its amounts (Amount, a value by key, a constant, arithmetic) with its game hook's code on both branches; Kunai, Shuriken, Monologue and Game Piece fixed, 18 differences reviewed.
- [x] Monologue played more than once: each copy (an instanced power) gives its own Strength on every card you play (two copies, 2 Strength a card); the adapter reads one copy's value (SimReaders.AddVars writes each copy over the last), so the predictions give 1. Review every instanced power with a model the same way (Covered, Imitation Learning, Magic Bomb, The Bomb, Withering Presence; the counters, Orbit, Automation, Cacophony and Panache, are read per copy already).
- [ ] Kunai, Shuriken and Game Piece read their values by the game's keys now (decision #182, part B2): no change in play today (each value is 1, the fallback the wrong keys gave), but a value a game update changes is followed.
- [x] A scan of every value key a model names (`Var(..., "Key")` in `Sim/Models`) against its game class's real keys (CanonicalVars): the Kunai and Monologue kind of wrong key, even where no effect is compared.
- [ ] Dark Embrace at the end of your turn: the game draws for the Ethereal cards exhausted together (AfterSideTurnEnd), the simulator as each one exhausts (same count; a drawn Burn could act in the simulation and not in the game).
- [x] Play Preview with a controller or keyboard: an untargeted card previews once selected (NControllerCardPlay plays it on confirm); targeted cards once aimed.
- [x] Play Preview (decision #183): the held card's outcome on Incoming Damage, Teammate Incoming Damage, the Death Skull and the enemies' intents; checked by the play check. Test in game: the intent overlay's place and font, the tint, untargeted and targeted cards.
- [x] Listeners (decision #184, part B3 of #182): cards in your piles listen after your powers and relics, in the game's order; Void and Regret fixed; Iteration and Pagestorm in their place.
- [x] Names players read (decision #185): Keyword Tags, Proc Tags, Shuffle Tag, Potion Tags, Prediction Progress and the Forecast, Play and Coverage audits, ids kept as the settings keys; Ethereal Fade split out of Keyword Tags; Run Reminders split into Reward Tags, Event Reminders, Shop Reminders and Map Reminders, each starting as the old switch was; on-card and potion tabs are tags wherever a player reads them; the dev dashboard shows the names (`FeatureNames.Of`).
- [ ] Test in game (decision #185): an old settings file (Card Indicators off, Run Reminders off, a place's group switch off) loads into the new switches as it was; the Outside fights page (four switches, the note naming those off, Inspect and the event chance under Event Reminders); Quick setup's Between fights rows; the dashboard's names, and ids on hover.
- [x] The settings menu by where each thing shows (decision #186): In fights in sections (on your cards, with every tag kind under its feature; on enemies; beside you; while you hold a card; under End Turn), Between fights, Tags by source (one page, a filter row), General with the audits and dev mode; Quick setup by group; the Guide in the same order.
- [ ] Test in game (decision #186): the Tags by source filter row and a search across sources (headings, column headers, All on / All off per source), Quick setup's group switches, the On your cards section greyed out with Tags on cards off, and the Guide's new order.
- [x] Settings menu, parent and child (decision #187): family switches head their sources on Tags by source (each list they cover); Plain and Crossed tags labelled as filters under Tags on cards; every option's row and needs from one table (`MenuOption`, `SettingsOptionTests`); Tracker boxes and reminder columns grey out with their feature; an icon per row (the tag art on tag rows, kinds drawn as tags); Reset defaults puts back every option.
- [ ] Test in game (decision #187): the tag art icons (`CardOverlayRenderer.Standalone`: size, the part a card covers cut off, the slash, the text tags 2× and 40%, the overlapping family icons), the teammate portrait, the % corner on the chance icons, the column alignment of the indented source rows under their headers, the Tags by source button under Tags on cards.
- [ ] Drum of Battle with several cards exhausted: the game runs each card's exhaust and its reactions in turn; the simulator all exhausts, then Drum's energy (differs at 0 energy when Dark Embrace draws a Void). One exhaust action per card.
- [ ] Cards a play returns to your hand (Make It So, Right Hand Hand, Whistling Arrows) never enter the simulated hand: the hand's size, room for later draws and Regret's count miss them.
- [ ] Withering Presence: a Wither made into your hand during the turn isn't added to the cards acting at the end of the turn (its damage isn't simulated); then Aeonglass upgrading it as a monster listener.
- [ ] Ethereal cards exhausted at the end of the turn don't lower the simulated hand's size.
- [x] Instanced powers (decision #182, B2 follow-up): every copy counted (`.Total` values, `VarTotal`); The Bomb's copies each count down and go off; Covered keeps each covering player; Imitation Learning reads "copies you" from any copy; `InstancedPowerTests` holds every instanced power model to the game's instance type. Value-key scan (`ModelValueKeyCheck`): 67 key uses, none wrong.
- [ ] Monologue replayed while a copy is already there: the replay's new copy gets its Strength after it lands, so the total misses it by one (keep Monologue's copies apart, like the counter powers).
- [ ] The value-key scan on main's models, and the adapter's own value keys (`Sts2State.Var` in `SimReaders.Read`) in a table the scan reads.
- [x] Card programs, value tables (2026-11-23): 17 more per-card tables read from the program and deleted, each proven exact on both branches first (`CardEffectsTests.EachRuleFindsExactlyTheTableItReplaced`), with a catalog snapshot of every card's facts and play (`CardPlaySnapshotTests`) unchanged by each swap: draws and shuffles (AllyDrawCards, TeammatesDrawCards, ShuffleCards, ShuffleWhenShortCards), pile moves (TakenToHandCards, PutBackCards, MovedElsewhereCards, WholeHandCards), made cards (SadnessCards, ForgeCards), attacks (XHitCards, SingleHitRepeatCards) and the rest (PlaysFromDrawPile, AllPlayersSummonCards, KillStarCards, DislodgeCards, KeepsStretchCards). The reader now keeps a choice's arguments, the cards a play makes, an attack's hit count, what a loop goes over, piles by type, and locals stored once.
- [x] Card programs, conditions as values (2026-11-23): a step's condition is read as an expression (an or of ands of each branch's test, its direction kept, with what it tests: properties, card values, comparisons, lambdas, locals with where they were stored, an awaited result), from every way through the method; a loop keeps its round's test. `ProgramEvaluator` evaluates one against the board, each query translated once. The card play snapshot also reads every card replayed and with a teammate.
- [x] Card programs, draws on a condition (2026-11-23): `CardsDrawn` reads each draw for you from the program with its condition evaluated; ConditionalDrawCards, TargetDrawCards and ten `CardsDrawn` cases deleted (Normality no longer counts 3 draws; Scrawl from your draw pile one fewer).
- [ ] A replay's later plays see your hand as it was before the first (Restlessness, Impatience, Scrawl replayed): the cards the first play draws aren't in it.
- [x] Card programs, other cards exhausted (2026-11-23): `ExhaustOthers`, `ExhaustPickOf` and `ExhaustTakesNoActingCard` read from the program (`CardEffects.ExhaustsOfOthers`: a pick, a roll or a loop over a collection, its filter evaluated); Flak Cannon now counts its statuses.
- [x] Card programs, cards discarded, transformed and moved into your hand (2026-11-23, round three): `DiscardCount`, `TransformCount`, `TakenToHandCount` and the adapter's `HandTakes` table read from the program (`CardEffects.Discards`, `Transforms`, `TakesIntoHandOf`: one rule over every command that takes cards, with `ExhaustsOfOthers`: a pick from its pile with its filter, a roll, each card of a collection, the cards a draw gave, the top of your draw pile looked at), each proven against its table on both branches; the outcome snapshot unchanged. Guards' and Neow's Fury's "you choose how many" and The Hermit's either-or pick are read from the code too. The Hermit with only Curses in hand no longer counts a discard.
- [ ] A pick's discards on a replay's later plays aren't counted (Acrobatics, Survivor, Prepared replayed: one discard, the later plays none; `PlayFacts.DiscardCount` with `pickEachPlay: false`, as the table it replaced did).
- [x] Card programs, orbs (2026-11-23, round three): channels, evokes and slots read from the program (`PlayFacts.OrbsOf`: each command's orb and its loops' rounds on the board); `SimAdapter.Channels`, `RepeatChannels`, `SlotChange`, the evoke switches and `ChannelCount`'s cases deleted, proven against the tables on both branches; outcomes unchanged (Glacier, Meteor Strike and Chill now one channel action of several orbs). Upgraded Tempest's extra Lightning counts for Metronome.
- [x] Card programs, block gains (2026-11-23, round three): `BlockGainCount` counts each gain of the program on its condition and its loops' rounds; `RepeatedBlockCards`, its cases and `Fool.BlockWithoutVar` deleted. Bone Shards and Sacrifice with Osty missing gain no block.
- [x] Card programs, hits (2026-11-23, round three): `HitsPerPlay` reads the attack's condition and its hit count's value from the program; its cases, the glow tables and the branch rule `AttackHitCountsAreVars` deleted; snapshots unchanged.
- [x] Card programs, drawn cards and auto-plays (2026-11-23, round three): `DrawnCardReaders` read from the test of the draw's result (`SimAdapter.DrawnCardReaderOf`), the auto-plays' count, exhaust and upgrade from their command (`SimAdapter.AutoPlaysOf`); snapshots unchanged.
- [x] Card programs, actions from their steps (2026-11-23, round four, decision #193): each command step of a card's code is translated into its action (`SimAdapter.Steps`: `CommandsTranslated`; a command with none is a named gap, `CommandsNotTranslated`, `StepTranslationTests` on both branches), its values from the step's arguments and its condition guarding it where the code puts it. `PlayOrder`, `PlayConditions.Guard`, the facts' gating, `LaterAttacks`, `AttackOf`/`BlockOf`/`PowersOf`/`OrbsOf`, `CardEnergy.AddTo` and `TeammatesShivCards` deleted; `ConditionRuleTests` holds the adapter off the facts' presence counts. Fixes named in the 2.0.0 changelog (amounts read from the step: Expose, Friendship, Dominate, Hang, No Escape...; your side includes you: Huddle Up, Blade Symphony, Glimpse Beyond, Outrage; Panic Button's No Block; Hobbling Shot's debuffs on its shot's arrow; Bad Luck, Beckon and Haunt cost no HP when played).
- [ ] Card programs, next (docs/CARD_PROGRAMS.md "What code can't say"): GeneratedCount's rest (made cards for each player of your side are their step's now), made cards held to their step's condition (Shiv.CreateInHand's count parameter), then SplashCards, OverkillReaders and RepeatPerKillCards; Malaise's first Apply (arguments the reader leaves unread: the target's Strength loss isn't simulated); `CommandsNotTranslated` (CardCmd.Upgrade, CardCmd.AutoPlay, OstyCmd.Summon, PowerCmd.ModifyAmount, CreatureCmd.Heal and the rest).
- [ ] Decisions, Decisions: one simulation of the whole play, the card's draws before your pick, the pick's auto-plays, and what the card's finish sets off (Black Hole on its stars, its count toward Pale Blue Dot), so the play check can hold its state and powers after it. Skipped today with a reason (`ProcAudit.PickApart`). No tab for the card itself (the owner: what to triple is the player's call).
- [ ] Decision #172 follow-up: check in the first dev session that the engine's pipeline-compilation counters are running totals (`compiles` stays small and mostly 0 once the game has settled); read the next unexplained stall against them and the hook times.
- [ ] Decision #108, phase 3: a dev-only flag hotkey that bookmarks the last events and the simulator's start state for a note later; exporting a flagged or reported start state as a replay case into `tests/SpireQoL.Sim.Tests/Cases` (the page can write there once given the repo folder).
- [ ] Decision #87 stage 4: Incoming Damage's distribution (each amount's chance) on hover.
- [ ] Decision #87 stage 2 follow-up: whether lethal chance tabs are too many in roll-heavy fights (Lightning, Juggernaut): they show whenever the card surely raises a death's chance; a floor (say 25%) may be needed.
- [ ] Decision #87 stage 3 note: drawing into a full hand stops the draw (the card stays in the draw pile: CardPileCmd.Draw), while a card added to a full hand goes to the discard pile (CardPileCmd.Add: Shivs, generated cards). Draw reactions (Iteration: a Status drawn draws more) run inside the draw, before the next card: with hand space short, order matters there.
- [ ] Decision #107, numbers to set from the `[sim] searched` and `[sim] cap:` logs: SimJob.FirstBudget (20,000 copies before a background turn), the background share of cores (half), the memory cap (150,000 states held together) and the work cap (3 million copies a search).
- [x] **Proc tabs** (0.39.0): Fertilizer and All Is Well (Sadness), Mighty Wings (a 30+ hit), Whittling Knife (shuffles), Compost (kills); Sown's energy for Pael's Tears; transformed cards count as created.
- [x] **HP loss during a play** (0.45.0): the card's HP cost in the simulator; Tungsten Rod, Self-Forming Clay, Emotion Chip, Centennial Puzzle, Demon Tongue, Red Skull, Rupture and Inferno tabs from the HP you really lose (enemy Thorns past your block included, after Tungsten Rod and Buffer).
- [x] **Free cards** (0.46.0, decision #44): Free Card (Tune), Free Attack, Free Skill tabs with the energy saved, `?` under Snecko Eye, crossed when wasted.
- [x] **Rest Preview** (0.46.0, decision #45): HP after resting and the heal wasted; the boss fight's start with Pantograph or Bath Robes.
- [x] 1.6.0 (decision #57): Rest Preview as a sum beside your character, with relic and boss-relic lines; Mend preview on a hovered teammate (checked as `rest-mend`); no second rest offered while the heal plays out.
- [ ] Play test: Rest Preview's sum beside your character (and a teammate's with Mend); free-card `?` and crossed tabs.
- [x] 1.6.1: Guide's Sozu slash centred; Retain and Sly example card with both keywords in its text.
- [x] 1.6.2 (decision #58): eggs and Fresnel Lens beside an event option only when a card they change can come from it (CardAffinity, option cards read from the code; scan in the catalog tests).
- [x] 1.6.3: Sea Glass read as its own character's pool; filters holding a per-option value (Glass Eye) keep the whole pool.
- [x] 1.7.0 (decision #59): card odds under event icons; Strike Dummy, Miniature Cannon, Mystic Lighter, Vitruvian Minion beside event options (made cards, modifier interactions, upgrade and enchant options).
- [x] 1.7.1 (decisions #60, #61): rewards built by the game (pool relics counted), Glass Eye per rarity, Astrolabe upgraded, Lighter and Cannon by eligible Attacks, one transform number.
- [ ] Test in game: the chance under event icons (Colorful Philosophers, Infested Automaton, Room Full of Cheese, Sea Glass); Mystic Lighter beside Tezcatara's soup.
- [x] 1.8.0 (decision #62): tabs on offered cards (rewards, event choices, shop) for Strike Dummy, Fake Strike Dummy, Miniature Cannon, Mystic Lighter, Vitruvian Minion.
- [ ] Test in game: boost tabs on a card reward, the shop and Sea Glass's grid; that they leave with the screen and never sit on deck-pick screens.
- [x] 1.9.0 (decision #63): card relic checks (card-rule, odds log, exact icons in the event check); offered-card tab switches per place; Chance under event icons option.
- [x] 1.10.0 (decision #64): the Inspect screen (groups, marks, chances, relic tabs and mark buttons, transform picker by card).
- [ ] Test in game: Inspect at Colorful Philosophers, Room Full of Cheese, Glass Eye and a transform (Pandora's Box, an event transform); that Esc closes it and the game's keys are blocked while open.
- [ ] Look across runs: `[audit] [odds]` lines (predicted chance against how often a card came up).
- [ ] Test in game: the Guide page's Rest Preview, reminders examples (Reward Tags, Event, Shop and Map Reminders) and the green 0 on the free-card examples.
- [ ] **Dread mode:** boss stats and moves change in Dread runs (read live from intents); Dominion powers reviewed in the ledger. Queen's Dread opening applies 99 Frail, Weak and Vulnerable.

---

## 17. Open display decisions (2026-11-10, from play testing)

- [x] **Odds: decided by decisions #73 and #88** (chances wherever they're knowable, lethal tabs included). Was: ~~deferred, still open (decision #22).~~ Chances are shown in one place only: beside an enemy's HP skull, for "dies before acting if you end your turn now" (Death Skull). Card tabs show certainties only: the lethal tab means this card (some aim of it) is needed for an enemy to be certainly dead before it acts; crossed tabs still warn when something is possible in any branch (Ravenous on Dualcast with two Corpse Slugs). Not doing for now: chances on tabs, enemy skulls following the hovered or dragged card, a kill-count spread. The odds depend on where the card is aimed and how the rolls fall, which quickly gets hard to read and to remember; certain outcomes are the common, simple case. Revisit only with a new reason.
- [~] **Showing deaths that happen anyway** → Death Skull (in test). Example: Punch Construct at 2 HP with your orbs about to finish it at end of turn: no skull on your attacks (the lethal tab means "this card makes the kill happen"), but now a skull beside its HP. Branch weights in the simulator give the chance when a roll decides it.
- [x] 0.31.3 (first play test of 0.31.2): audit uses the prediction captured when the play's action starts (Feral returned cards to hand before the check); Focus changes in the simulator (Hyperbeam, found by the after-play check); no after-play state check when the fight ends; coverage false alarms (Pael's Legion relic/monster name, "proc:" rule keys, relic property getters); card-code findings: debuffs read from card code (Uppercut, Putrefy, Shockwave, Malaise), Modded draws, Scrape discards, Voltaic channels, Fisticuffs and Entrench block. Open: Zap predicted an evoke (full slots) that didn't happen, right after FTL drew it; check the next report's before-state.
- [x] 0.31.4 (auto-reports): Forge cards count their first Sovereign Blade as a created card; transformed cards (Begone, Charge, Guards, Seance, Compact, Primal Force) count toward the proc check's created-card budget only.
- [x] 0.31.5 (auto-reports): Spinner channels a Glass orb on play only when upgraded (SpinnerPower channels at turn start); Chill channels one Frost per hittable enemy.
- [x] 0.31.6 (auto-reports, audit layer): a play that ends the fight isn't held to its predicted proc damage or block (the game's history skips the killing hit: `CreatureCmd.Damage` doesn't log once `CombatManager.IsEnding`); Flutter is a simulator-compared power (the after-play check compares its stacks), so its silent decrement on a hit isn't "unexplained". Charge's created-card report from 0.31.3 was already fixed in 0.31.4.
- [x] 0.31.7 (auto-reports, audit layer): the played card's cost rising after the play is the game's "until played" cleanup (`CardEnergyCost.AfterCardPlayedCleanup`; Splash's free Hemokinesis); the outcome audit stops counting an enemy's end-of-turn hits at its killing hit (Waterfall Giant's Steam Eruption brings it back at 999999999 HP and later orbs hit it). Guards' created-card report from 0.31.3 was already fixed in 0.31.4.
- [ ] **Waterfall Giant coming back in the simulator.** With Steam Eruption, killing it sets it to 999999999 HP with the About to Blow move (`SteamEruptionPower.AfterDeath` → `TriggerAboutToBlowState`), and it stays a target for random hits (Lightning). The simulator treats it as dead, so with other enemies alive the later random hits' branches would be off (the encounter is the boss alone today).
- [x] **Enemy Thorns in the simulator** → 0.31.8. Hitting a Thorns enemy with a card's attack costs you block (ThornsPower.BeforeDamageReceived, unpowered, before each hit), but the simulator doesn't model it, so your block after the play and the damage you take at end of turn can be off. Found by the after-play check (FTL into Spiny Toad, Thorns 5, 4 block: predicted 4 block left, actual 0).
- [x] **Card scan for forging, transforming and upgrade-only effects** → 0.31.9. The Forge and transform lists from 0.31.4 had no scan check, so a new card that forges or transforms wouldn't have been reported; Spinner was named in the channel check instead of being tested upgraded.
- [x] 0.31.10 (act 2 of the 0.31.9 play test): Metronome's tracker row hides after it fires; the power check explains the stat a temporary Strength/Dexterity/Focus power applies (Dark Shackles, #100).
- [x] 0.31.11 (act 3 of the 0.31.10 play test): FTL's conditional draw; Reboot's shuffle (PlayChain.ShufflesFirst, scan check for CardPileCmd.Shuffle); the proc check explains death hooks of powers present before the play (Dampen) and keyword/cost changes of an upgrade; orb slot changes in the simulator (Bulk Up, Capacitor, Modded; OrbOp.Slots, scan check for OrbCmd.AddSlots/RemoveSlots).
- [x] 0.31.12: draw and channel shortfalls are reported (decision #19; FTL's play limit only showed in the log); draws capped at the cards in both piles; a character without orb slots gets one on the first channel (OrbCmd.Channel), in the simulator.
- [x] 0.31.13: Thunder (tab and simulator; was a known gap); relics' after-play draws (Iron Club, Game Piece) come after your powers' hand additions (Calamity) in PlayChain; Artifact blocking temporary debuffs (Dark Shackles) explained.
- [x] 0.31.14: Update Notice on the main menu (GitHub releases, once per main menu visit; decision #20).
- [x] 0.31.15: Update Notice says "up to date" when GitHub confirms it.
- [x] 0.31.16: Update Notice shows test-build diagnostics (version, commit, uncommitted changes, build time, newest release).
- [x] 0.31.17 (Ironclad run): Feel No Pain and Flame Barrier powers from their plain values (card scan reports a tracked self power with no amount); Colossus in intent changes; X-cost cards left out of the card-cost check.
- [x] **Multiplayer: teammates' status** → built as Teammate Incoming Damage (0.31.27, decision #30). Was: Hovering a teammate could show their Incoming Damage (what targets them, their block, powers and orbs are all on screen; their hand isn't, so only what doesn't depend on it). With several Defects, their orbs at end of turn decide kills too.
- [x] **Multiplayer: teammates' end-of-turn effects in the simulator.** From what's on screen: their orbs' passives (in player order, around yours), Hailstorm, The Bomb, Stone Calendar, Consuming Shadow's evokes, Gold-Plated Cables, Thunder on their evokes. Their block (Frost, Plating...) only protects them. Not counted, the non-optimistic side: anything that needs their hand (Screaming Flagon, Stampede's auto-play, their cards' end-of-turn effects), and their block-gain reactions (Juggernaut on their Frost). Open: whether a teammate's hand (or its size) is shown in multiplayer; if it is, Stampede and Screaming Flagon could count.
- [x] Multiplayer: hovering a teammate hid your HP bar (NCreature.SetRemotePlayerFocused) and moved Incoming Damage and Relic Counters off your character while still showing your numbers; they now stay anchored to your character.
- [x] 0.31.19: simulator performance for multiplayer (decision #23): copy-on-change orbs and teammates, a 32-branch sample after the 128 cap following one outcome per step; performance test for four Defects with 40 Lightning orbs.
- [x] 0.31.20 multiplayer step 1 (decision #24): reports tagged with players and characters; coverage reviewed for teammates (Covered/Intercept covered through the damage hooks; Beacon of Hope a gap).
- [x] Multiplayer step 2 → 0.31.21 (decisions #24, #25): teammates as full players in the one simulation, using their real hand for the audits and their hand size (not contents) for what's shown; teammate checks skipped for teammates known to run Spire QoL (the host sees clients' mods in the handshake; clients don't): their block, HP, Osty, end-of-turn block (and Beacon of Hope sharing it with you), each hit per player through the game's damage hooks (Tank, Intercept, Covered, Guarded), cards you play that change damage to players (Tank, Intercept) in the intent changes; the end-of-turn check compares every player.
- [x] Multiplayer step 3 (0.31.27, decision #30): hovering a teammate shows their Incoming Damage, without their hand; checked on every game, their Osty included.
- [x] 0.31.27: Parrying Shield tab (plain/crossed from the simulator before and after the play).
- [x] 0.31.27: teammates' Cloak Clasp, Ripple Basin and Parrying Shield; a parity test (you vs a teammate, every end-of-turn effect and retaliation).
- [x] 0.31.28 (decision #31): performance tests count work (copies, steps, bytes), not time; a per-step copy budget. Fixed Shatter (4^20 copies in one step) and Beacon of Hope with Juggernaut (out of memory with four players).
- [x] (Done: the simulator exhausts them, decision #38.) ~~Multiplayer: a teammate's Ethereal cards (Feel No Pain, Charon's Ashes, Forgotten Soul at end of turn) aren't simulated for them;~~ it depends on their hand (checks only, never shown).
- [x] 0.31.26 multiplayer audit review: a teammate's action during your choice (decision #29, logged and counted); teammates' block and HP in the state after your play; coverage scan flags ally-targeted cards (AnyAlly: Believe in You, Blaze, Concoct, Constellation, Coordinate, Demonic Shield, Fade, Ignition, Imitation Learning, Intercept, Largesse, Lift, Mimic, Soulbound, Tutor; AllAllies: Blade Symphony, Energy Surge, Glimpse Beyond, Huddle Up, Legion of Bone, One for All, Plot, Rally).
- [x] Multiplayer: ally-targeted cards in the simulator (0.37.3, 0.37.4): Lift, Constellation, Demonic Shield, Ignition, Mimic and Intercept are modelled; every other AnyAlly card has a reviewed reason in CardCoverage.TeammateTargetModelled. AllAllies reviewed: Self Deprecate (0.42.1, #300: Joy into teammates' hands, created by you); the rest are still listed.
- [x] Multiplayer: the enemy-turn check compares a teammate's Osty too (0.31.27); their block counts through their HP.
- [ ] Multiplayer: confirm in a co-op game that the end-of-turn check compares the last frame after every player has ended.
- [ ] Multiplayer step 2 leftovers: cards you play that change damage to players (Tank on you; ~~Intercept on a teammate~~, 0.37.4); a teammate's Osty and their Fading/Feel No Pain block (from their hand: not in what's shown, decision #25); per-teammate checks of block and Osty.
- [ ] Multiplayer: plays resolve one at a time through a shared queue. A teammate's queued, unresolved play can change what yours does (Beacon of Hope → their Juggernaut on your block card), and latency widens the gap between what's on screen and what resolves. Tabs for effects through teammates (their Juggernaut from your Beacon), and treating teammates' queued plays like your own quick plays (logged, not reported, unless the history shows otherwise).
- [x] **Move the proc rules into the simulator (decision #26).** Done in 1.0.0 (decisions #48, #49): every rule is a model, tabs come from the play log. About 100 trigger rules in ProcIndicatorsFeature become simulator rules, family by family (on-play counters, energy/cost triggers, exhaust/discard reactions, HP-loss, block...), each with tests; first extend the single-player golden test to cover tabs so no move changes them unnoticed. Then teammates' triggers through chains (Beacon of Hope → their Juggernaut) come for free.
- [x] **Multiplayer guardrails (0.31.22):** separate per-session report budget for multiplayer (a noisy co-op session must not use up single-player reports), a `multiplayer` label on those issues, and a "Multiplayer predictions (experimental)" setting (on) for step 3 and later.
- [x] Dropped with the lead-in (0.31.25, decision #28): a teammate's queued card and a client's unconfirmed card in the lead-in. Tabs show the board; the audit predicts each play as it starts.
- [x] 0.31.23: Necrobinder findings: Sacrifice and Bone Shards kill Osty in the simulator (card scan reports other cards that kill a creature); summons explain Osty's HP and max HP in the proc check; the stale check counts potions and teammates' plays too.
- [x] 0.31.24 (found by driving a test fight): a queued card ran itself in its own lead-in; the proc check now takes the tabs from the moment a play is queued (ActionQueueSet.ActionEnqueued), not its last frame in hand.
- [x] Won't do (2026-11-11): use outcomes already visible mid-resolution. Tags serve decisions between plays, and the gap lasts only while a play resolves (under a second); doing it needs a partial-play model (which of the resolving card's effects have happened) across every card type, new subtle code in the core for something a player can't act on. Since 0.31.25 tabs show the board as it is (decision #28). Was: during Sword Boomerang, after the first hit went to the other Myte, the Death Skull still showed 12% (all three hits on it) instead of 0%. Account for the hits already resolved and branch only on the rest (needs how far the resolving play has got).
- [x] Fixed (build only): the test-build note didn't show for an incremental dirty build (the DLL had no GitDirty attribute): StampGitDirty likely runs but the generated assembly-info file isn't regenerated. Fix in Directory.Build.targets.
- [x] 0.31.25 (decision #28): each play's proc check uses a prediction made as its action starts, from the board then; the play queue reader and the lead-in are gone.
- [x] 1.20.8 (decision #103): the tabs shown on the card's last frame in hand are held to that prediction when nothing came between them (`tabs shown` issues); skips counted with their reason.
- [ ] Test in game: a quick play's check at resolution (two enemies, Sword Boomerang then a queued Strike; the summary's "against the tabs in hand" count should stay 0).


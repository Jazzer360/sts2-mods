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

## 1. Card modifiers → `card-indicators`

- [ ] **Decide: keep the Sly tab?** (2026-11-09) It doesn't fit "happens when you play this card". It helped when choosing cards to retain before Well Laid Plans changed; the game already makes Sly cards glow when you pick cards to discard. Stays for now.

### Keywords
The game shows these only as card text. `CardModel.Keywords` merges local keywords (on the card) with global ones (granted by powers), so a single read catches both.

- [~] **Ethereal translucency**: any card that is Ethereal right now renders at reduced opacity, whatever the source.
  - Sources: canonical Ethereal cards; Music Box (adds Ethereal to its copy); Hexed affliction via `HexPower` (granted globally, gone when Hex is removed).
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

## 3. Condition relics → `proc-indicators` (tabs on cards in hand; Relic Reminders was merged into Proc Indicators)

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
| [~] | Unsettling Lamp | cards that apply a debuff to an enemy | the first card that debuffs an enemy this combat applies double the debuff. Doesn't trigger or get spent against a target with Artifact. Note that on the card |
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
- [~] **Constrict** (debuff, after your turn ends): blockable (`Unpowered`).
- [~] **Magic Bomb** (debuff): blockable.
- [~] **Disintegration** (debuff, late end of turn): blockable.
- [~] Unblockable, listed separately: Regret (HP loss = cards in hand), Bad Luck, Beckon, Demise. Poison is already previewed by the game.
- [ ] Not damage, but worth a reminder: Doubt (Weak), Shame (Frail), Debt (lose gold).
- [~] **Doesn't count enemies Poison kills before they act** (with Hard To Kill per-hit cap and Hardened Shell per-turn cap). Doom does NOT prevent attacks.
- [~] **Retaliation cancels remaining hits**: Thorns (before each hit; the killing hit still lands), Flame Barrier and Reflect (after each hit), through the game's damage hook.
- [ ] **Health-bar preview** (the summary line now sits under the HP bar; a bar overlay is still open): render the expected loss after block alongside the game's existing Poison/Doom preview in `NHealthBar`.

---

## 6. Shuffle warning → `shuffle-warning`

- ~~Draw-pile counter~~: dropped in 0.3.0 (the card badges are enough).
- [~] Discard pile tab on draw cards in hand whose draw would exceed the draw pile (forcing a reshuffle).
- ~~KEPT marker on other hand cards~~: dropped in 0.12.0 (the draw-pile tab is enough).
- [x] Every draw card: all `CardPileCmd.Draw` callers, including Prepared, Dagger Throw, Escape Plan, Pillage, Scrawl, Compile Driver, Restlessness, Huddle Up, Mad Science (Wisdom).
- [ ] Draw potions (need a potion-slot tab).

---

## 7. Intent forecast → `intent-forecast`

Knowable information only (CLAUDE.md): it may show the moves an enemy *can* make next and their odds, worked out from its move rules and what you've seen it do. Never the move it will actually pick, its RNG roll, or hidden state a player couldn't infer.

- [ ] Confirm monster AI structure in `MonsterMoves/` (state machine vs. scripted).
- [ ] Deterministic next moves.
- [ ] Random branches with odds.
- [ ] Conditional moves.

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

## 9. Proc indicators → `proc-indicators` (built in 0.19.0; full source list in FEATURES.md)

**Goal:** show, on each card in hand, every power or relic that playing it would set off. "Damage is damage, block is block": if a card's play gains block or deals damage through a proc, the card should say so, including procs one step removed (a card that *exhausts another card* while Feel No Pain is up).

Two kinds of proc:
- **Trigger only:** "this card sets off that relic," like the existing relic reminders. Tab with the relic's or power's icon, no number. Examples: Intimidating Helmet, Mini Regent.
- **Quantified:** the proc gains block, deals damage or grants a stat, and the exact amount is worth knowing. Example: Feel No Pain on True Grit shows +3 block (+4 upgraded, +6 with two copies of the power).

### Decided (2026-11-09): source icon + number

Quantified procs use **option A**: one tab per source, with the source's own icon (power or relic) and the amount as a number (e.g. Feel No Pain icon, `3`). Trigger-only procs keep the plain icon, with no number. CLAUDE.md's UI rules and docs/FEATURES.md "On-card tabs" allow a number on quantified proc tabs only. Revisit option B if cards end up with too many tabs.

### Configuration: every indicator can be turned off (defaults: everything on)

Players should be able to trim what shows, down to single indicators. Defaults show everything.

- [~] **Per-tab toggles** (0.19.0): `Tabs` in settings, one key per tab kind (today: `retain`, `sly`, `replay`, `shuffle`, `double-play:<Source>`, `proc:<Source>`; `exhaust`, `relic:` and `blocked` are gone), added as each kind first appears, default on. Replaced `ShowDoublePlay` and `DimBlockedCards` (migrated); `ShowInnateEternalBadges` removed with the tabs.
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
| [ ] | Not yet | | Deeper chains (a proc's damage setting off another proc), Beacon of Hope / Hammer Time (multiplayer). |
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

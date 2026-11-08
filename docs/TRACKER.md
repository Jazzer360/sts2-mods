# Tracker

The master work list for Spire QoL. Check an item off only when it is implemented, documented in [FEATURES.md](FEATURES.md), and noted in [CHANGELOG.md](../CHANGELOG.md).

Everything below was found in the decompile of game **v0.111.0**. Class names are the game's own (`MegaCrit.Sts2.Core.Models.*`). Thresholds come from each class's `CanonicalVars`. Re-check them after game patches.

Legend: `[ ]` to do · `[~]` built, needs an in-game check · `[x]` done · **(verify)** behavior inferred from code, confirm in game.

---

## 0. Core: settings and layout (`core/`, used by every feature)

- [~] **Settings storage**: per-feature on/off and options, saved to a JSON file in the mod's own folder (not in game saves). Survives game updates.
- [ ] **In-game settings menu**: an entry in the game's settings/mods screen. **(verify)** whether the game has a mod-settings hook, or whether we add our own button.
- [~] **Movable widgets**: every free-floating indicator (relic counter tray, incoming-damage readout, block forecast, shuffle counter) is a `MovableWidget`.
  - [~] **Unlock layout** toggle (menu and hotkey): widgets show an outline and can be dragged. **Lock** freezes them and makes them click-through.
  - [ ] **Anchor**: each widget pins to a screen corner/edge/center, or to a game element (player health bar, hand, draw pile, relic bar). Positions are stored as anchor + offset, so they survive resolution and window-size changes.
  - [ ] **Snap**: optional grid snap while dragging.
  - [~] **Scale and opacity** per widget.
  - [~] **Reset**: per widget and "reset all" to defaults.
  - [~] Off-screen guard: a widget dragged or loaded outside the visible area is pulled back on screen.
- [ ] On-card indicators (badges, overlays) don't move freely. Their slot (top-center, corners) is a setting.
- [~] **Hide all overlays** hotkey.

---

## 1. Card modifiers → `card-indicators`

### Keywords
The game shows these only as card text. `CardModel.Keywords` merges local keywords (on the card) with global ones (granted by powers), so a single read catches both.

- [~] **Ethereal translucency**: any card that is Ethereal right now renders at reduced opacity, whatever the source.
  - Sources: canonical Ethereal cards; Music Box (adds Ethereal to its copy); Hexed affliction via `HexPower` (granted globally, gone when Hex is removed).
  - Must still be readable. Opacity is a setting (default ~70%).
- ~~Ethereal badge~~: dropped in 0.3.0; translucency alone marks Ethereal.
- [~] **Retain badge**: from the Retain keyword or single-turn retain (`ShouldRetainThisTurn`).
  - Adds Retain: Anointed, Calculated Gamble, Dredge, Gold Axe, Misery, Monologue, Reaper Form, Royal Gamble, Scrawl, Times Up, Wish, Wisp; enchantments Royally Approved and Steady.
  - Single-turn retain: Expertise.
- [~] **Sly badge**: from the Sly keyword or single-turn Sly (`IsSlyThisTurn`; Hand Trick).
- [~] **Exhaust badge**, only when Exhaust was *added* (Goopy enchantment), not for cards that always exhaust.
- [~] **Innate / Eternal badges**: off by default (they matter in the deck view, not mid-combat).
  - Adds Innate: Afterimage, Aggression, Arsenal, Beacon of Hope, Big Bang, Call of the Void, Entropy, Hello World, Infinite Blades, Juggling, Machine Learning, Mad Science, Royally Approved, Soulbound, Speedster, Tyranny.
  - Adds Eternal: Tezcatara's Ember.
- [ ] **Keyword removed** marker, e.g. Exhaust stripped. Removes keywords: Apparition, Chill, Demonic Shield, Discovery, Echo Form, Fusion, Graveblast, Hologram, Hotfix, Ignition, Know Thy Place, Mimic, Mirage, Prolong, Rainbow, Secret Technique, Secret Weapon, Soul's Power, Thinking Ahead, Transfigure, Underworld, Void Form, Voltaic. **(verify)** whether these remove a keyword from *other* cards or only from themselves. Only the first case needs a marker.

### Cost and play-count changes
The game already recolors changed costs (blue = increased, green = decreased, red = can't afford). We add *duration* only.

- [ ] **"Free this turn"** marker. Sources: Abundance, Attack/Skill/Power/Colorless potions, Bullet Time, Crossbow, Discovery, Distraction, Infernal Blade, Jeweled Mask, Liquid Memories, Mad Science, Mummified Hand, Orobic Acid, Splash, Vexing Puzzlebox, White Noise.
- [ ] **"Free this combat"** marker. Sources: Metamorphosis, Touch of Insanity.
- [ ] **Temporary cost change**: small clock glyph on the cost while the change expires at end of turn or on play. Sources: Snecko Oil and Enlightenment (this turn / until played); Flatten, Pinpoint, Stomp (this turn); Bookmark, Rocket Punch, Slumbering Essence (until played).
- [ ] **Global cost modifiers**, shown on affected cards. Powers: Borrowed Time, Corruption, Curious, Free Attack, Free Power, Free Skill, Tangled, Veilpiercer, Void Form. Relics: Brilliant Scarf, Spiked Gauntlets.
- [ ] **Replay count** (card plays more than once) badge. Sources: Hidden Gem, Soldier's Stew, Sword Sage power, Transfigure.
- [ ] **"Plays twice" next play**: Burst, Duplication, Echo Form, One-Two Punch, Signal Boost, Tag Team powers. Mark the cards in hand this applies to. (Throwing Axe is handled under one-shot relics, §3.)

### Afflictions (monster-applied)
All seven already have game overlays (`AfflictionModel.OverlayPath`). Our job is to stay out of their way and add the "can't play right now" state the game doesn't show. **(verify)** in game which of these the game already dims.

- [~] **Bound** (Chains of Binding): after one Bound card is played this turn, other Bound cards can't be played. Dim them.
- [~] **Ringing**: after any card is played this turn, Ringing cards can't be played **(verify the exact rule)**. Dim them. Clears at end of turn.
- [~] **Smog** (Smoggy): Smog skills can't be played. Applied to all skills after a skill is played. Dim them.
- [ ] **Entangled** (Tangled): attacks cost +N. The game already recolors the cost; no extra work unless the overlay is unclear.
- [~] **Hexed** (Hex): grants Ethereal → handled by translucency. Confirm the overlay and translucency look right together.
- [~] **Galvanized** (Galvanic): playing the power card deals N damage to you. Add a damage glyph so it's clear it hurts.
- [ ] **Tainted**: skills; playing one applies Tainted (+N damage taken from attacks). **(verify)**

### Enchantments (run-long, already have a game tab)
Shown by the game's `%Enchantment` tab, so no new indicator. Only make sure our badges don't overlap it.

---

## 2. Counter relics → `relic-counters` (tick-bar tray above the hand)

Every relic here exposes `ShowCounter`, `DisplayAmount` and `Status` (the game sets `Active` one step before it fires). Each relic gets one condensed bar: icon plus tick-marked segments that fill as the counter advances. The bar **flashes** when the next qualifying card fires it. Counters that carry across turns or combats are always shown and sort first.

| Done | Relic | Counts | Fires at | Resets | Effect |
| --- | --- | --- | --- | --- | --- |
| [~] | **Pen Nib** | attacks | 10th | never (carries across combats) | next attack deals double. **Blink + glow attacks in hand when ready**; see `relic-reminders` |
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
| [~] | Joss Paper | exhausts | 5th | **(verify)** | draw |
| [~] | Galactic Dust | stars spent | 10 | **(verify)** | gain block |
| [~] | Metronome | orbs channeled | 7th | each combat | deal damage |
| [ ] | Pael's Legion | cooldown (turns) | 2 | after use | block multiplier **(verify)** |

### Turn-counter relics (same tray, counting turns)

| Done | Relic | Fires | Effect |
| --- | --- | --- | --- |
| [~] | **Stone Calendar** | end of turn 7 | deal damage. Pips for turns 1–7; blink all of turn 7 ("fires when you end this turn") |
| [~] | Happy Flower | every 3 turns | +1 energy |
| [~] | Fake Happy Flower | every 5 turns | +1 energy |
| [~] | Pendulum | every 3 turns | extra draw |
| [~] | Pollinous Core | every 4 turns | +2 draw |
| [~] | Horn Cleat | turn 2 | gain block (also feeds `block-forecast`) |
| [~] | Captain's Wheel | turn 3 | gain block (also feeds `block-forecast`) |
| [~] | Candelabra | turn 2 | +2 energy |
| [~] | Chandelier | turn 3 | +3 energy |
| [~] | Sparkling Rouge | turn 3 | +Strength/+Dexterity |

---

## 3. Condition relics → `relic-reminders` (icons on cards in hand)

| Done | Relic | Show on | Meaning |
| --- | --- | --- | --- |
| [~] | **Pen Nib (ready)** | attacks | pulsing border in a non-game color: "this attack uses Pen Nib" |
| [~] | Art of War | attacks | struck-through icon while no attack played this turn (playing one loses next turn's energy) |
| [~] | Ripple Basin | attacks | struck-through icon: playing an attack loses the end-of-turn block |
| [ ] | Rainbow Ring | missing card types | attack/skill/power played this turn; highlight the missing types |
| [ ] | Mummified Hand | powers | playing a power makes a random card in hand free |
| [ ] | Game Piece | powers | draw on power play |
| [ ] | Lost Wisp | powers | damage on power play |
| [ ] | Daughter of the Wind | attacks | block per attack |
| [ ] | Ivory Tile | cards costing ≥3 | refund energy |
| [ ] | Helical Dart | Shivs | +Dexterity |
| [ ] | Razor Tooth | attacks/skills | upgrades the card when played **(verify types)** |
| [ ] | Chemical X | X-cost cards | +2 to X |
| [ ] | Strike Dummy / Fake Strike Dummy | Strikes | +damage |
| [~] | Velvet Choker | all cards | dim everything once the 6th card is played |
| [~] | Cloak Clasp | hand | end-of-turn block per card left in hand (feeds `block-forecast`) |
| [ ] | Screaming Flagon | hand | damage at end of turn if hand is empty |
| [ ] | Tingsha, Tough Bandages | discard | discard triggers **(verify)** |
| [ ] | Charon's Ashes, Forgotten Soul | exhaust | exhaust triggers **(verify)** |

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
| [ ] | Rainbow Ring | missing card types | playing an attack, a skill and a power in the same turn gives +Strength/+Dexterity (once per turn) |
| [ ] | Mini Regent | (stars) | triggers on stars spent once per turn **(verify)** |
| [ ] | Regalite | (generated cards) | block when a card is generated, once per turn **(verify)** |

---

## 4. End-of-turn block sources → `block-forecast`

Shows "+N at end of turn" beside current block, so you don't overblock.

- [~] **Plating** (power): gain N block at end of turn **(verify decay rule)**.
- [~] **Frost orb passive**: each Frost gives `PassiveVal` block, times the passive trigger count (`Hook.ModifyOrbPassiveTriggerCount`).
- [~] **Orichalcum / Fake Orichalcum**: block if you have none at end of turn.
- [~] **Cloak Clasp**: block × cards in hand.
- [~] **Ripple Basin**: block if no attack played this turn.
- ~~**Horn Cleat / Captain's Wheel**~~: not end-of-turn block (they fire at the start of turn 2/3, after enemies attack). Countdown lives in Relic Counters.
- [ ] **Rage / Skittish** powers: block on card play/hit **(verify)**; shown on cards, not in the end-of-turn total.
- [ ] **Sturdy Clamp**: block isn't cleared. Show carried block for next turn.
- [ ] **Parrying Shield**: deals damage if you end with ≥N block. Show its threshold.

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
- [~] Badge on draw cards and potions in hand whose draw would exceed the draw pile (forcing a reshuffle).
- [~] While a shuffle is possible, mark the cards in hand that will stay out of the new draw pile.
- [ ] Find every draw source: cards using `CardPileCmd.Draw` and draw potions, and which ones have a fixed amount.

---

## 7. Intent forecast → `intent-forecast`

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
- [~] Badges go top-center; relic overlays top-corner; nothing covers the cost, title, enchantment tab or affliction overlay.
- [ ] At most one relic overlay icon per card; several stack with a count.
- [ ] Everything works on enlarged (hover) cards and in grid/pile views.
- [~] A global hotkey hides all overlays.

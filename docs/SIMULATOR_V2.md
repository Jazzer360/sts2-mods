# Simulator v2 (Spire QoL 1.0.0)

The design and the plan for rebuilding the outcome simulator around the game's own structure: an interpreter that runs a
card's effects step by step through the same hooks and pipelines the game uses. Decision #48. This file is the plan of
record while the work is in progress; once 1.0.0 ships it stays as the simulator's design document.

## Why

The simulator (decision #26) is the one model behind every prediction, and it's been right far more often than not. But
almost every report in the last stretch came from one of two structural causes:

1. **A play is a summary, not a sequence.** `SimAdapter.Play` reads the game once, before the card is played, and boils
   the whole play down to fixed numbers (hits of N per enemy, block of M, procs). Anything that changes *during* the play
   needs its own patch: `HitDamageLater` (Impaled arrows, Vulnerable from the card, Vigor and Gigantification spent, Slow),
   `HitDamageByPlay` (Tender, Pen Nib, Mighty Wings), `BlockLater`/`BlockByPlay` (Stretch, Pael's Legion, Vambrace,
   Tender), `EnchantmentBlockLater`, `GazeHits` (Monarch's Gaze), and a 150-line block of enemy-intent adjustments
   (`AdjustedHits`: Weak, Strength loss, Colossus, Surrounded, Intercept, Tainted). `SimPlay` has 39 fields.
2. **Two models of the same thing.** Proc Indicators' ~100 rule cases predict relic and power effects separately and feed
   their amounts into the simulator as fixed numbers. When they disagree we get double counts (Inferno) or rules that
   ignore the simulated state (Tungsten Rod and your block).

Each fix was right, but each was found by a player's report. The game itself is regular: damage, block and HP loss go
through fixed pipelines of per-source terms, and everything else goes through ~180 named hooks. A simulator that mirrors
that structure makes these interactions fall out instead of being patched one at a time.

## Goals and non-goals

Goals:
- **Per-hit math from the game's own pipelines.** Damage, block and HP loss are worked out at the moment they happen, from
  the simulated state, with the same terms in the same order as the game.
- **Hooks with the game's names.** Every relic, power and enemy effect the simulator knows is a small model that
  implements the game hooks it overrides, written from its decompiled body.
- **Cards as scripts.** A card play is a list of actions (attack, gain block, apply a power, draw, exhaust, create, channel,
  evoke, lose HP...), run once per play with the hooks in between. Replays, auto-plays and lead-in plays are the same thing.
- **Tabs from the simulation.** A model that fires (flashes) during the simulated play gets the tab; its amount is what
  the simulation attributes to it. One model instead of two.
- **Unknown stays exact.** A damage/block source the simulator doesn't model keeps the game's own number for this card and
  target (captured term), so nothing falls back to a guess; it only can't *change* mid-play. The coverage ledger lists
  every source with a reviewed classification.

Non-goals:
- Running the game's own engine on a copy of the fight (rejected: global singletons, history, saves, Steam stats, UI
  calls without null checks, RNG; and it breaks observe-only, decision #10).
- Reading the RNG or anything hidden (CLAUDE.md "Only knowable information"): random outcomes stay branches.
- Changing what's shown. Tabs, Incoming Damage, Death Skull, the lethal tab and the trackers look and mean the same; v2
  changes how their numbers are made.

## The game model being mirrored (decompile v0.113.1)

- **Damage** (`Hook.ModifyDamage` → `ModifyDamageInternal`): base; the card's enchantment (additive, then multiplicative);
  every listener's `ModifyDamageAdditive`; every listener's `ModifyDamageMultiplicative`; every listener's
  `ModifyDamageMultiplicativeLate`; the smallest `ModifyDamageCap`. Decimal throughout; `Math.Max(0, (int)x)` at the end.
- **Block** (`Hook.ModifyBlock`): base; enchantment; `ModifyBlockAdditive`; `ModifyBlockMultiplicative`; max 0.
- **HP loss** (`Hook.ModifyHpLost*`): BeforeOsty, BeforeOstyLate, (Osty takes his share), AfterOsty, AfterOstyLate; a
  source "modified" the amount when the truncated value changed (Tungsten Rod's flash).
- **Listener order** (`CombatState.IterateHookListeners`): for each creature (allies, then enemies): its powers; then the
  monster (enemies) or, for an active player, relics, potions, orbs, then cards in piles. "Powers fire before relics" is
  this order.
- **A card play** (`CardModel.OnPlayWrapper`): result location; play count (`ModifyCardPlayCount`); then for each play:
  `BeforeCardPlayed`; `OnPlay` (the card's commands, each awaited in turn, hooks nested inside); the enchantment's
  `OnPlay`; the affliction's `OnPlay`; card play finished; `AfterCardPlayed` (Early, normal, Late).
- **An attack** (`AttackCommand`): per hit: `BeforeDamageReceived` (Thorns), damage through the pipeline, block, HP loss
  pipeline, `AfterDamageGiven`, `AfterDamageReceived`, deaths; after the last hit `AfterAttack` (Vigor, Gigantification,
  Quietus are spent here).
- **End of turn** (`CombatManager.DoTurnEnd`): auto-play phase; `BeforeSideTurnEndEarly`; `BeforeSideTurnEnd`; orb passives
  per player; Ethereal exhausts and cards acting in hand; debuffs on you; `AfterSideTurnEnd`; `AfterSideTurnEndLate`; then
  the enemy turn (poison tick, block cleared, each enemy's move, hit by hit).

## Architecture

### State (`Sim/SimState.cs`)
`SimState` keeps its shape (players: you and teammates; enemies; orbs; branch results), so the outputs every consumer reads
don't change. Added:
- `Powers` on each creature: power name → amount plus a small per-power data slot (counters a model keeps), for every
  power a model reads or changes (Strength, Dexterity, Vigor, Vulnerable, Weak, Frail, Slow, Impaled, Aim...).
- `Models` on each player: the relic models with their counters (Pen Nib's count, Kunai's count, Pael's Legion armed...).
- The play and attack in progress, players' turn counters (cards started and finished this turn, by type and tag), and an
  **event log** for the current play (which model flashed, damage and block it caused).

### Models (`Sim/Models/`)
`SimModel` has virtual methods with the game's names and meaning: the pipeline terms (`ModifyDamageAdditive`,
`ModifyDamageMultiplicative`, `ModifyDamageMultiplicativeLate`, `ModifyDamageCap`, `ModifyBlockAdditive`,
`ModifyBlockMultiplicative`, `ModifyHpLostBeforeOsty`, `ModifyHpLostAfterOsty`, `ModifyHpLostAfterOstyLate`,
`ModifyPowerAmountGiven`) and the hooks (`BeforeCardPlayed`, `AfterCardPlayed`, `BeforeDamageReceived`, `AfterDamageGiven`,
`AfterDamageReceived`, `AfterAttack`, `AfterBlockGained`, `AfterBlockBroken`, `AfterCardDrawn`, `AfterCardExhausted`,
`AfterCardDiscarded`, `AfterCardGenerated`, `AfterStarsGained`, `AfterOrbChanneled`, `AfterOrbEvoked`, `AfterDeath`,
`AfterArrowShot`, `BeforeSideTurnEnd`, `AfterSideTurnEnd`...). A hook pushes actions (it never changes state behind the
interpreter's back) and may `Flash()`. Each model is written from its decompiled body and names it in a comment. Models
are stateless code; their numbers live in the state (`Powers`, relic counters), so branches copy only numbers.

### The interpreter (`Sim/SimEngine.cs`)
A script's actions run in order on the branch set; each action is a step (or a few), and a hook that may branch (a random
target) returns its branches, chained with the existing merging step (`SimEngine.Then`), so identical branches merge after
every step and the copy budget, truncation and the safe run (decisions #29, #31) work as before. Hooks that run inside
damage (BeforeDamageReceived, the HP-loss pipeline, AfterDamageGiven, AfterDamageReceived, AfterBlockBroken) are
deterministic and run synchronously inside `SimEngine.Damage`, so every source of damage (hits, procs, orbs, end of turn)
gets them. (The first draft had an explicit per-branch work stack; composing over branch sets did the same with no new
machinery.)

### Pipelines
- **Damage on a hit** = the game's formula over: the base (the card's value, or a calculated value worked out by the
  adapter for that target), the enchantment's terms, the **modelled** sources' terms computed from the current state, and
  the **captured** terms of every unmodelled source (read from the game for this card and target: that source's own
  `ModifyDamage*` call). Same for block and HP loss.
- **Enemy attacks** use the same pipeline with the enemy as dealer and you (or a teammate) as target, so Weak or Strength
  loss a card applies, Colossus, Surrounded, Intercept and Tainted change the enemy's hits by the same rules, with no
  special code.

### Card scripts (`Sim/Scripts` + `Procs/ScriptBuilder.cs`)
The adapter turns a card into a script: generic actions from its dynamic values (Damage/HitCount → attack; Block → gain
block; PowerVar<T> → apply power; Cards → draw...) checked against the commands its `OnPlay` calls (the card coverage scan
already reads them), with per-card scripts where the code does something else (today's per-card switches in `PlayFacts`
and `SimAdapter`, moved). A card whose script isn't known gets the facts it has now and is listed by the coverage check.
The script runs once per play (play count from the game: `ModifyCardPlayCount`), with `BeforeCardPlayed` and
`AfterCardPlayed` around each play, then the enchantment's and affliction's scripts.

### Outputs
`SimOutcome` keeps every field consumers read today (kills, block breaks, stuns, HP, taken, Death Skull chances...). New:
the play's **event log** per branch (model flashes with their damage, block, summons; cards drawn, exhausted, created;
energy and stars), merged into "in every branch" and "in some branch" sets. Proc Indicators builds tabs from it; the play
check compares it with what the game did.

## The adapter (`Sim/SimAdapter.cs`)
Reads state only (CLAUDE.md: rules live in the simulator):
- creatures, powers and relics into `Powers` and models (a reader per modelled class: amount, counters, armed flags);
- for each card the simulation may play (the hovered card, lead-in plays, auto-plays): its script, its base values per
  target, and the captured terms of unmodelled sources per target;
- enemy intents: base damage per hit and the captured terms of unmodelled sources on each player.

## Tabs and audits
- **Proc Indicators**: tabs from the hovered card's simulated play: a model that flashed in every branch (good) or in some
  branch (warning), with the amounts the log attributes to it. The 100-case `Evaluate` switch goes away; each rule becomes
  (part of) a model. Tabs that aren't about a model firing (Retain, Sly, replay, return, free card, shuffle, lethal) keep
  their own code but read the simulated play where it has the answer.
- **Proc check** (`ProcAudit`): compares the game's flashes and effects with the simulated play's event log (the same
  vocabulary on both sides).
- **Forecast check**: unchanged interface (it compares the simulator's end-of-turn and enemy-turn outcome with the game's).

## Coverage
- Every damage, block and HP-loss modifier source in the game has a reviewed classification in the coverage ledger:
  **modelled** (a model computes its term from the state) or **captured** (fixed for the turn: its term is read from the
  game per card and target). First pass (v0.113.1):
  - Modelled (can change during your turn): Strength, Dexterity, Vigor, Gigantification, Aim, Quietus, Vulnerable, Weak,
    Frail, Pen Nib, Slow, Lethality, Phantom Blades, Stretch, Pael's Legion, Vambrace, Serrated Arrows, Wah Hammer,
    Tracking, Colossus, Surrounded, Covered, Intercept, Tainted, Unmovable, Flutter, Hard to Kill, Intangible, Shadowmeld,
    Double Damage, Undying Sigil, One for All.
  - Captured (fixed for the turn): Accuracy, Calcify, Conductor's Baton, Conqueror, Dislike You, Flanking, Guarded, Hang,
    Honing, Knockdown, Leadership, Shrink, Soar, Tank, Strike Dummy, Fake Strike Dummy, Miniature Cannon, Mystic Lighter,
    Vitruvian Minion, Fasten, No Block, Mock Revive, multiplayer scaling.
- Every class with combat hooks keeps its ledger entry (covered / ignore / gap); "covered" now names the model.
- A source new in a game update is captured (exact for the turn) and reported by the coverage check until reviewed.

## Plan

Built on `main` without intermediate releases; 1.0.0 ships when every step is done. Each step keeps
`dotnet test tests/SpireQoL.Sim.Tests` and `tests/SpireQoL.Catalog.Tests` passing.

1. **Interpreter core.** Work stack, actions, checkpoints and merging, models and hook dispatch in listener order, the
   damage/block/HP-loss pipelines, the event log. `SimPlay` becomes the input format for a legacy-style play and is
   compiled to a script (`SimPlay.ToScript`), so every existing test and the single-player golden run on the new engine
   unchanged. Exit: all 259 tests and the golden pass on the interpreter; performance tests within budget (limits raised
   only with a stated reason).
2. **Effects as models.** Move what the engine hard-codes into models dispatched by hooks: end of turn (Plating,
   Orichalcum, Cloak Clasp, Ripple Basin, Hailstorm, The Bomb, Screaming Flagon, Stone Calendar, Obsidian Vice, Lunar
   Pastry, Parrying Shield, Consuming Shadow, Pael's Eye, Feel No Pain, Charon's Ashes, Forgotten Soul, debuffs on you),
   enemy reactions (Curl Up, Sneaky, Skittish, Thorns, Burrowed, Asleep, Slumber, Flutter, Plow, Shriek, The Gambit,
   Slippery, Ravenous, Crab Rage, Explosive), HP loss (Tungsten Rod, Beating Remnant, Buffer, Intangible, Hardened Shell,
   The Boot), Juggernaut, Envenom, Inferno, Thunder, Beacon of Hope, Mighty Wings. Exit: the same tests pass.
3. **Dynamic damage and block.** The modelled sources above (Strength, Vulnerable, Pen Nib...) and the captured terms.
   The adapter stops precomputing hits; `HitDamageLater`, `HitDamageByPlay`, `BlockLater`, `BlockByPlay`,
   `EnchantmentBlockLater`, `GazeHits`, `SetHits` and `AdjustedHits` are deleted. Exit: tests for every modelled source
   (each written from its decompiled body), the existing ones still passing.
4. **Card scripts.** `ScriptBuilder` (from `PlayFacts`' per-card knowledge) builds each card's script; the adapter feeds
   scripts for the hovered card, lead-in plays and auto-plays. Draws, exhausts, discards, created cards, stars and energy
   become actions with hooks.
5. **Rules into models.** Each Proc Indicators rule becomes (part of) a model; tabs come from the event log; `Evaluate` is
   deleted. The settings catalog check keeps every model that can tab tied to a settings row.
6. **Audits.** The proc check compares the event log with the game's history; the forecast check reads the same outcome.
7. **Coverage and docs.** Ledger classifications for every modifier source; FEATURES (simulator, Proc Indicators,
   technical notes), CLAUDE.md's simulator section, DEVELOPMENT, TRACKER, CHANGELOG 1.0.0, decision #48. Release 1.0.0.

## Risks
- **Regressions in what's shown.** Accepted during the build (the owner's call); the golden and the ported tests guard the
  engine, and the in-game checks report what's left after release.
- **Performance.** The interpreter does more work per hit (pipelines from state). Guarded by the copy/step budgets and the
  performance tests; models hold no per-branch objects beyond numbers; power sets are immutable and shared between branches.
- **Captured terms that really change mid-turn.** The classification makes this a reviewed list; a wrong "captured" shows
  up as a report naming the source, and the fix is a model.
- **Scripts for unusual cards.** Cards with choices, random picks or odd code keep explicit scripts or are reported by the
  card coverage check, as today.

## Progress
- [x] 1. Interpreter core (power sets, models, hook dispatch in listener order, pipelines, play log, scripts)
- [x] 2. Effects as models for everything that happens during your turn: damage (enemy reactions, HP-loss modifiers, players' HP loss, Inferno), deaths (Crab Rage, Ravenous), enemy intents (Enrage, Monarch's Gaze, Vital Spark). Juggernaut, Thunder and Necro Mastery log their hits. The end-of-turn and enemy-turn pipeline (Plating, Orichalcum, Parrying Shield, Consuming Shadow, debuffs on you, Beacon of Hope, Explosive, Flame Barrier, Reflect, your Thorns) stays engine code in the game's hook order (decision #49); the powers it reads are the players' power sets (no second copy).
- [x] 3. Dynamic damage and block: models for every source that changes during a turn; the adapter builds real plays (captured terms, per-target bases), intents are pipeline inputs.
- [x] 4. Card scripts: each play's card events (energy, draws, discards, exhausts, created cards, stars, shuffles, dislodged arrows) are actions with hooks; models make their own (Dark Embrace's draw, Calamity's cards, The Sealed Throne's stars). Orbs, Osty, Judgement and Capture Spirit are still summary parts of the play.
- [x] 5. Rules into models: every Proc Indicators rule is a model (`Sim/Models/ProcPowerModels.cs`, `ProcRelicModels.cs`); tabs come from the play log; `Evaluate` is gone. Effects judged by the end of the turn (kills, stuns, waking, Parrying Shield, HP-loss relics) compare outcomes. Unsettling Lamp doubles debuffs.
- [x] 6. Audits: the proc check reads the log-based tabs (same keys and ranges); the damage-formula audit compares each card's simulated first hit with the game's preview.
- [x] 7. Coverage, docs, release 1.0.0.

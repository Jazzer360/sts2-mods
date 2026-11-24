# The outcome simulator (since Spire QoL 1.0.0)

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
- Changing what's shown. Tabs, Incoming Damage, Death Skull, the lethal tab and the trackers look and mean the same; the simulator
  changes how their numbers are made.

## The game model being mirrored (decompile v0.113.1)

- **Damage** (`Hook.ModifyDamage` → `ModifyDamageInternal`): base; the card's enchantment (additive, then multiplicative);
  every listener's `ModifyDamageAdditive`; every listener's `ModifyDamageMultiplicative`; every listener's
  `ModifyDamageMultiplicativeLate`; the smallest `ModifyDamageCap`. Decimal throughout; `Math.Max(0, (int)x)` at the end.
- **Block** (`Hook.ModifyBlock`): base; enchantment; `ModifyBlockAdditive`; `ModifyBlockMultiplicative`; max 0.
- **HP loss** (`Hook.ModifyHpLost*`): BeforeOsty, BeforeOstyLate, (Osty takes his share), AfterOsty, AfterOstyLate; a
  source "modified" the amount when the truncated value changed (Tungsten Rod's flash).
- **Listener order** (`CombatState.IterateHookListeners`): for each creature (allies, then enemies): its powers; then the
  monster (enemies) or, for an active player, relics (not melted), potions, orbs, then the cards in every pile (hand, draw,
  discard, exhaust, play), each followed by its affliction and enchantment. "Powers fire before relics" is this order. The
  simulator's list (`SimEngine.Listeners`) is the same: each player in player order (their powers in the order applied,
  their relics, then their cards: `SimPlayer.Cards`, the card classes in their piles with a `CardListener` model, one per
  class since each copy's hook answers only its own card's events), then each living enemy's powers. Its kinds are `ListenerKind`.
  A dead enemy's powers aren't listeners (the game removes them at its death: `CreatureCmd.Kill`, `Creature.RemoveAllPowersAfterDeath`),
  for every model: no model asks whether its owner lives. The hit that killed it and its own `AfterDeath` are told to them still
  (`SimEngine.ListenersWith`), as the game runs both before the removal; a hook that started before an enemy died skips its powers
  from then on. A power that stays past its owner's death (`ShouldPowerBeRemovedAfterOwnerDeath`) answers no hook in the simulator
  (`DeathListenerTests`, catalog).
  `ListenerOrderTests` (catalog tests) compares it with the game's list for a fight built from the game's own objects.
  - Cards with a model: Void (its `AfterCardDrawn` loses energy after your powers' and relics' draw reactions: Automation,
    Iteration, Pagestorm) and Regret (its `BeforeSideTurnEnd` counts the hand before the Ethereal cards exhaust).
  - No potion, orb or monster has a model: no orb class overrides a hook (passives run from `DoTurnEnd`, `OrbQueue`), the
    one potion with combat hooks (Fairy in a Bottle: `ShouldDie`) has no power or relic answering the same hook before it
    (Lizard Tail's is `ShouldDieLate`), and the monsters' hooks only change music, textures or talk, or (Queen) a flag and a move
    that change no hit this turn: each read as a program, its coverage ledger entry held to what its code does (`MonsterHookTests`,
    decision #195).
  - Card hooks kept elsewhere, in the game's order: Drum of Battle's energy on its exhaust (after the exhaust's listeners;
    a play exhausting several cards runs every exhaust's listeners first, the game each card's in turn), Epic Retrieval's energy on a dislodged arrow (`SimCardPlay.ArrowEnergy`, after Yah), Howl from Beyond and
    I Am Invincible at the auto-play phase (after Stampede and Simpleton), the enchantments' `AfterCardPlayed` (`PlayGrowth`,
    only their own card's later plays).
- **A card play** (`CardModel.OnPlayWrapper`): result location; play count (`ModifyCardPlayCount`); then for each play:
  `BeforeCardPlayed`; `OnPlay` (the card's commands, each awaited in turn, hooks nested inside); the enchantment's
  `OnPlay`; the affliction's `OnPlay`; card play finished; `AfterCardPlayed` (Early, normal, Late).
- **An attack** (`AttackCommand`): per hit: `BeforeDamageReceived` (Thorns), damage through the pipeline, block, HP loss
  pipeline, `AfterDamageGiven`, `AfterDamageReceived`, deaths; after the last hit `AfterAttack` (Vigor, Gigantification,
  Quietus are spent here).
- **End of turn** (`CombatManager.DoTurnEnd`): auto-play phase; `BeforeSideTurnEndEarly`; `BeforeSideTurnEnd`; orb passives
  per player; Ethereal exhausts and cards acting in hand; `AfterSideTurnEnd` (Constrict, Magic Bomb, Demise with the other
  powers and relics); `AfterSideTurnEndLate` (Disintegration, Insatiable Dominion); then
  the enemy turn (poison tick, block cleared, each enemy's move as its code runs it: hit by hit, with the powers and block it gives in
  their place).

## Architecture

### State (`Sim/SimState.cs`)
`SimState` keeps its shape (players: you and teammates; enemies; orbs; branch results), so the outputs every consumer reads
don't change. Added:
- `Powers` on each creature: power name → amount plus a small per-power data slot (counters a model keeps), for every
  power a model reads or changes (Strength, Dexterity, Vigor, Vulnerable, Weak, Frail, Slow, Impaled, Aim...).
- `Models` on each player: the relic models with their counters (Pen Nib's count, Pael's Legion armed...).
- `Fields` on each player and enemy (decision #195): what the models read from their code keep of their own (Kunai's AttacksPlayedThisTurn), `Model.Field` → value, seeded from the game and part of the state key.
- The play and attack in progress, players' turn counters (cards started and finished this turn, by type and tag), and an
  **event log** for the current play (which model flashed, damage and block it caused).
- The **history ledger** (decision #191, `Sim/SimHistory.cs`, `SimEngine.History.cs`): the questions the board's models ask of the game's
  log of the fight (`SimState.History`: an entry kind, this turn or the fight, whose, the detail the code tests), each player's count
  of each (`SimPlayer.Turn`, `SimPlayer.Fight`) seeded once by the adapter from the game's log and counted forward by the simulator's
  own events where the game logs them; this turn's counts start over as the enemies' turn starts. A condition reads one as a count
  (`CountCondition` of `SimCounted.History`).

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

Since decision #195 a model is read from the game's code where the reader can (`ReadModel`, Sim/Programs): its hooks' programs (`ModelSpec`, pure data) are read once per class from the running game's code by the adapter (`HookReader`, `ReadModels`), compiled into delegates, and run through the same engine functions (`ProcBlock`, `ProcDamageAll`, `ProcCardEvent`, `ApplyPower`); `SimModels` holds both kinds, a class being one or the other: every power and relic with no hand-written model whose hooks read whole is read (a rule, `ReadModels.Specs`), and one the reviewed builds read whole that the running game's code doesn't (a game update) makes every simulation it's in unknown, with why (`ModelSpec.Unread`, `SimModels.UnreadReason`). In the game they're read on a worker as the mod loads (`SimModels.StartReading`): the game's thread doesn't spend the few tenths of a second, a search on a worker that asks first waits for the read, and the frame makes no prediction until the read is over (`SimWait.Models`, out of its 8 ms: the card tags and the turn forecast are pending until then, never made without the models). What the read makes is published once and never changed. The simulator's tests run the read models from `tests/SpireQoL.Sim.Tests/ReadModels/read-models.json`, written and checked by the catalog tests (docs/CARD_PROGRAMS.md C6).

### The interpreter (`Sim/SimEngine.cs`)
A script's actions run in order on the branch set; each action is a step (or a few), and a hook that may branch (a random
target) returns its branches, chained with the existing merging step (`SimEngine.Then`), so identical branches merge after
every step (decision #107: one merged search, bounded by its memory and work caps). Hooks that run inside
damage (BeforeDamageReceived, the HP-loss pipeline, AfterDamageGiven, AfterDamageReceived, AfterBlockBroken) are
deterministic and run synchronously inside `SimEngine.Damage`, so every source of damage (hits, procs, orbs, end of turn)
gets them. (The first draft had an explicit per-branch work stack; composing over branch sets did the same with no new
machinery.)

A run is the play, the end of your turn, the enemies' turn start, then the enemy turn. The enemies' turn start
(`SimEngine.EnemyTurnStart`, CombatManager.StartTurn) runs every listener's BeforeSideTurnStart, AfterSideTurnStart and
AfterSideTurnStartLate, its participants the living enemies; each enemy's poison then ticks and the enemies move. The
players' next turn start (AfterEnergyReset, AfterPlayerTurnStart, BeforeHandDraw) is past every simulation.

### Pipelines
- **Damage on a hit** = the game's formula over: the base (the card's value, or a calculated value worked out by the
  adapter for that target), the enchantment's terms, the **modelled** sources' terms computed from the current state, and
  the **captured** terms of every unmodelled source (read from the game for this card and target: that source's own
  `ModifyDamage*` call). Same for block and HP loss.
- **Enemy attacks** use the same pipeline with the enemy as dealer and you (or a teammate) as target, so Weak or Strength
  loss a card applies, Colossus, Surrounded, Intercept and Tainted change the enemy's hits by the same rules, with no
  special code.
- **Pipeline steps** (`Sim/SimPipelines.cs`, docs/CARD_PROGRAMS.md B1): each game command the simulator mirrors (GainBlock,
  Damage on an enemy and on a player, an attack, PowerCmd.Apply, Draw, Exhaust, Discard, GainEnergy, Channel) lists the game
  hooks its simulated version runs, in order. `PipelineParityTests` (catalog tests) reads each command's `Hook` calls from the
  game's code on both branches and fails on a hook missing, extra or out of order that isn't modelled or reviewed there.

### Card scripts (`Sim/Scripts` + `Procs/ScriptBuilder.cs`)
The adapter turns a card into a script: generic actions from its dynamic values (Damage/HitCount → attack; Block → gain
block; PowerVar<T> → apply power; Cards → draw...) checked against the commands its `OnPlay` calls (the card coverage scan
already reads them), with per-card scripts where the code does something else. Since decisions #182 and #193 the script is the card's
program's steps, each translated into its action (`SimAdapter.Steps`); since decision #194 nothing is decided about a card before its play
beside them: the program's counts on the board are `BoardCounts` (Programs), the card's prediction `CardPrediction`, and the play check's
counts its simulation's log (`SimKeys.PlayEvent`, `PlayOrbChanneled`, `FrostEvokeBlock`, `OrbEvokeDamage`).
The script runs once per play (play count from the game: `ModifyCardPlayCount`), with `BeforeCardPlayed` and
`AfterCardPlayed` around each play, then the enchantment's and affliction's scripts. A card play's shape is decided once before its first play, as
`OnPlayWrapper` does (decision #192): where the card goes after, from its own result pile (`ModifyCardPlayResultLocation`: Feral, Nostalgia,
Rebound, Corruption's Skills to the exhaust pile) and the play count the simulator's own models add (`ModifyCardPlayCount`: Echo Form on the history
ledger, Burst, Duplication, One-Two Punch, Signal Boost, Ki Verse, Tag Team, Throwing Axe), each branch its own.

Its actions are built from the card's own code, one command step at a time, in the order the code runs them (decisions #182,
#193, docs/CARD_PROGRAMS.md): `ProgramReader` reads the card's `OnPlay` (its state machine, local functions and the delegates it
hands to commands) from the running game into a program, and `SimAdapter.Steps` translates each command step into the simulator's
action for that command (`CommandsTranslated`: an attack from `DamageCmd.Attack` with its hits and targeting from its builder, a block
from `CreatureCmd.GainBlock` to the creature its argument names, a power from `PowerCmd.Apply<T>` with its amount argument, a draw, a
take, a channel...), its values from the step's arguments (block before attack for Iron Wave, exhausts before attack for Fiend
Fire, block after each exhaust for Second Wind's loop). A command with no action yet is a named gap (`CommandsNotTranslated`), and
`StepTranslationTests` holds every command every card calls on both branches to one or the other. Whether a step's action exists is
never decided on the board before the play: its condition is.

A step the card's code runs only on a condition is an ordinary action inside a `WhenAction` (decision #190, `Sim/SimConditions.cs`):
the condition is a `SimCondition` (the last attack killed, Osty missing, the target impaled, a count of your hand, of the cards a filter
lets through, of the plays finished this turn, and not, all, any of those), asked of each branch's own state as the action runs, so a
replay's later play asks it on the board the earlier plays left. A test of the card's code is asked once a play, where the code asks it
(its `At`): the answer is kept for the rest of the play (an else, a later step under the same if), and a local the code stores before
later steps is asked there (`AskAction`: Ki Hop's `wasImpaled`). Any condition guards any effect, so a new card's combination needs no
code.

### Enemy moves (decision #195)
An enemy's move is read from the game's code like a card's play: the method its `MoveState` performs (`_onPerform`) is read into a
program (`ModelPrograms`), and `SimAdapter.MoveOf` translates its steps into the enemy turn's actions, in order (`SimEnemy.Move`): its
attack as its intent's hits (`MoveAttackAction`: one attack step for each attack intent, so the hits stay the game's numbers, run through
the damage pipeline as each lands), a power on the creatures its target argument names (`MovePowerAction`: `targets` are the living
players, `this.Creature` itself, `GetTeammatesOf` its side, a loop's items each), block on an enemy (`MoveBlockAction`, through the game's
block math when read). The enemy turn runs each enemy's move in turn order (CombatManager.ExecuteEnemyTurn), to its end even when your
Thorns killed it mid-attack (MonsterModel.PerformMove has no check; PowerCmd.Apply asks only that the fight isn't ending), so Vulnerable an
earlier enemy puts on you raises a later enemy's hit, and Strength given to an ally raises its hits. A move behind a hidden intent, one whose
attacks can't be placed against its intent, or one the reader couldn't follow is its intent's hits alone, as before. A command with no
action yet is a named gap (`SimAdapter.MoveCommandsNotTranslated`), and a power or block step that can't be built (on a condition, a power
made first) is reviewed in `EnemyMoveTests` as unable to change a later hit.

### Outputs
`SimOutcome` keeps every field consumers read today (kills, block breaks, stuns, HP, taken, Death Skull chances...). New:
the play's **event log** per branch (model flashes with their damage, block, summons; cards drawn, exhausted, created;
energy and stars), merged into "in every branch" and "in some branch" sets. Proc Indicators builds tabs from it; the play
check compares it with what the game did: every count it holds a play to (draws, exhausts, discards, made cards, orbs channeled, the
Frost block and the damage the play's evokes deal) is this log's, at the target the card was played at (decision #194).

## The adapter (`Sim/Adapter/SimAdapter.cs`)
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
    Double Damage, Undying Sigil, One for All, Soar and Leadership (read from their code by decision #195's rule).
  - Captured (fixed for the turn): Accuracy, Calcify, Conductor's Baton, Conqueror, Dislike You, Flanking, Guarded, Hang,
    Honing, Knockdown, Shrink, Tank, Strike Dummy, Fake Strike Dummy, Miniature Cannon, Mystic Lighter,
    Vitruvian Minion, Fasten, No Block, Mock Revive, multiplayer scaling.
- Every class with combat hooks keeps its ledger entry (covered / ignore / gap); "covered" now names the model.
- A source new in a game update is captured (exact for the turn) and reported by the coverage check until reviewed.

## Plan

Built on `main` without intermediate releases; 1.0.0 ships when every step is done. Each step keeps
`dotnet test tests/SpireQoL.Sim.Tests` and `tests/SpireQoL.Catalog.Tests` passing.

1. **Interpreter core.** Work stack, actions, checkpoints and merging, models and hook dispatch in listener order, the
   damage/block/HP-loss pipelines, the event log. `SimPlay` becomes the input format for a legacy-style play and is
   compiled to a script (`SimScript.Of`), so every existing test and the single-player golden run on the new engine
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
- [x] 4. Card scripts: each play's card events (energy, draws, discards, exhausts, created cards, stars, shuffles, dislodged arrows) are actions with hooks; models make their own (Dark Embrace's draw, Calamity's cards, The Sealed Throne's stars). Orbs, arrows, Capture Spirit, Judgement, Bouncing Flask and Osty's death are actions too (decision #50).
- [x] 5. Rules into models: every Proc Indicators rule is a model (`Sim/Models/ProcPowerModels.cs`, `ProcRelicModels.cs`); tabs come from the play log; `Evaluate` is gone. Effects judged by the end of the turn (kills, stuns, waking, Parrying Shield, HP-loss relics) compare outcomes. Unsettling Lamp doubles debuffs.
- [x] 6. Audits: the proc check reads the log-based tabs (same keys and ranges); the damage-formula audit compares each card's simulated first hit with the game's preview.
- [x] 7. Coverage, docs, release 1.0.0.
- [x] After 1.0.0: orbs, arrows, Osty and the remaining card effects as actions (decision #50); the end of turn as models and the old play format retired (decision #51).

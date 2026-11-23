# Card programs: a card plays in its own code's order (decision #182)

The simulator never decides the order of a card's effects. Each card's play is read from the game's code in the running game:
its commands in the order the code calls them, with where each amount comes from. The simulator runs each command through
the game's pipeline for it, with its hooks inside, in that order. When a card's code changes in a game update, or differs
between beta and main, the prediction follows with no change to the mod. Spire QoL 2.0.0.

## Why

The listener engine is right: hooks named as the game names them, listeners in the game's order, damage and block through
the game's pipelines on every hit. A double-played Iron Wave with Kunai at 2 already gains one more block on the second play:
Kunai fires in `AfterCardPlayed` between the plays, and Dexterity is live in the block math. Recent bugs came from elsewhere:

| What went wrong | Example | Source |
|---|---|---|
| The order of a play's effects | Fiend Fire (attacked before its exhausts), Iron Wave, Dash, Wah Cho, Wah Cho Ki (attack before block: the Thorns hit predicted unblocked; the crossed tab is right either way) | the adapter's fixed order |
| Which cards draw before they discard or exhaust | `DiscardThenDrawCards`, `ExhaustThenDrawCards`... | hand-kept lists |
| A pipeline missing a step | No Draw: the game's draw asks `Hook.ShouldDraw` first | a copied pipeline |
| An amount copied wrong | Vicious and Dark Embrace drew 1, not their amount | a copied hook body |

The first two are the adapter (`Sim/Adapter/SimAdapter.Play.cs`). It builds every play in one fixed order: HP loss, attack,
block, powers, orbs, energy, draws, cards leaving the hand, created cards. It also uses about 45 per-card tables (about 300
card names) in `Procs/PlayFacts.cs` and `Sim/Adapter/SimAdapter.cs`. This plan replaces that order with the card's own order,
and the tables with what the code says. The last two rows get checks against the game's code (Part B).

## The game's flow (what's mirrored)

- **A play** (`CardModel.OnPlayWrapper`): result pile chosen (`Hook.ModifyCardPlayResultLocation`); play count
  (`GeneratePlayCount`). Then, for each play: `Hook.BeforeCardPlayed`, the card's `OnPlay`, its enchantment's `OnPlay`, its
  affliction's `OnPlay`, then `Hook.AfterCardPlayed` and `AfterCardPlayedLate`. `SimEngine`'s `CardPlayAction` already has
  this shape.
- **A command** is a pipeline whose hooks run before the card's next line. For example, `CreatureCmd.GainBlock` runs
  `BeforeBlockGained`, then `ModifyBlock` (additive, multiplicative), then `AfterModifyingBlockAmount`, then the gain, then
  `AfterBlockGained`. `CardCmd.Exhaust`, `CardPileCmd.Draw` and `DamageCmd.Attack...Execute` work the same way. Nothing is
  queued: each `await` finishes, with everything it sets off, before the next line.
- **Listeners** (`CombatState.IterateHookListeners`): for each creature in order (allies, then enemies), its powers. Then its
  monster, or its player's relics, potions, orbs and the cards in every pile.

## Part A: card programs

### A1. Reading a program

`Procs/CardProgram.cs` reads a card type's play into a `CardProgram`, once per type and cached. It reads the loaded game's
code (`GameCode.Instructions` over `CardCode.OnPlayMethods`: `OnPlay`, its state machine and its local functions), so a player
on main gets main's program. It reads only code, never state or the RNG.

A program is a tree of steps:

- **`Command`**: one game command, kept by name (`CreatureCmd.GainBlock`, `PowerCmd.Apply<VulnerablePower>`, the attack
  builder down to `Execute`), with its arguments' sources.
  - A source is one of: a card value (`DynamicVars.Block`, `Damage.BaseValue`, `Repeat`, `Cards`, `PowerVar<T>`), a constant,
    a count the code works out (the hand's size, the cards a loop goes over), the target, or `Unknown`.
  - The attack builder's calls (`WithHitCount`, `Targeting`, `TargetingAllOpponents`, `TargetingRandomOpponents`, `FromCard`,
    `BeforeDamage`) become the attack's hit count, spread and hooks.
- **`Repeat`**: a loop. The count is a card value (`for i < Repeat`), the cards a pile holds (a `foreach` over a list taken from
  a pile: one step per card, its count read when played), or `Unknown`.
- **`If`**: a condition. Like a command, a condition is a **game query** (a property or method of the game's objects),
  translated once into a simulator test and used by every card that asks it. There is no per-card code.
  - What beta's cards ask: whether the card is upgraded (28 cards), whether the attack killed (Sunder, Feed, Hand of Greed,
    The Hunt, Knockout Blow, Timber: `Results...WasTargetKilled`), whether the target is dead or has a power, whether Osty is
    alive, X cost, orb, arrow and pile counts against a value, and whether Doom was applied this turn. About twenty queries;
    each one's cards are found by the coverage test, not listed.
  - Sunder therefore reads as `Attack`, then `If(killed) GainEnergy(Energy)`. The simulator already has the kill-reward
    action for this, so its energy follows the attack's kill in every branch.
  - Rules that need no list:
    - A branch with no effect command in it (a visual's null check, the fast-mode setting, test mode) doesn't change the
      outcome and is skipped.
    - A null check on what a `Choice` returned is "the player picked a card" (41 cards).
  - A query with no translation yet is `Unknown`: the steps behind it are not modelled, with the card and the query named.
  - **Conditions as values (2026-11-23).** A step's condition is a `ProgramCondition`: an or of ands of tests, each test a branch's
    own (`ConditionLiteral`: where it is, what it tests, which way). What a test tests is a `ProgramValue` read back from the
    instructions: a property or method of what it's asked of (`QueryValue`: `cardPlay.Target.IsDead`,
    `CreatureIsImpaledByArrow(cardPlay.Target)`), a card value (`Cards`), a constant (an enum's by name: `Type == Attack`), a
    comparison, arithmetic, a lambda with its own condition (`Any(hand, c => c.Type == Attack)`), what an awaited command gave
    back (`result of CardPileCmd.Draw`), an array, and a local or field stored once, with where it was stored (Ki Hop's
    `wasImpaled`, taken before its attack).
  - The conditions come from the method's flow, not from its layout: every way from the start to a step, each the tests it
    passes, with the machinery's jumps followed as they always go (an await resumes past its suspend, the dispatch starts at
    the top) and a return (a leave to the end) ending its way. Ways that meet again are merged and simplified, so both sides
    of an if leave nothing behind it, `a || b` reads as `a or b`, and `if (!x) return;` puts `x` on what follows (Restlessness).
  - A loop's body runs on the loop's own condition (Wrest's `while` inside its `if`: the loop and the draw after it both wait
    on the if's test); its round's tests are the loop's `While` (Pillage: the drawn card, its type, your hand's room). A way out
    of a loop goes on with the loop's condition.
- **Amounts the code works out** (Growth: `CalculationExtra × the cards exhausted`): simple arithmetic over known sources
  (values, counts, constants) is read. Anything else is an `Unknown` amount.
- **`Choice`**: a `CardSelectCmd` the player answers mid-play. Choices stay out of the model (decision #92: one decision, never
  a sequence), but a choice decides only **which** card. It doesn't decide **that** the play goes on.
  - The steps after it still run, and the hooks around the play still fire. Tuning Fork at 9 fires its block on Acrobatics,
    Ki Chant or Burning Pact; that is true in the simulator today too, checked.
  - A step that acts on the pick knows its count (one card discarded, one exhausted). So `Discarded` and `Exhausted` with
    their count, and what they set off (Tough Bandages, Feel No Pain, Dark Embrace), stay exact.
  - **A pick is "a card", never a particular one** (the owner's rule, 2026-11-23). The play does what any pick would do, and
    that is predicted exactly: the card leaves the hand, the Discarded or Exhausted event fires with its count, and what those
    set off (Tough Bandages, Feel No Pain, Dark Embrace, the counters) is included. No favourable or unfavourable pick is
    assumed. What depends on which card is chosen (its own discard or exhaust hook, such as Drum of Battle's energy; a Sly card
    playing itself; its cost) is neither simulated nor marked: no `NotModelledAction`, no `?`. Like the next card you play, it's
    the player's own decision (decision #92). Only when every card it could be acts alike is that what any pick does
    (`ExhaustPick.Chosen` with only identical Drums in hand).
  - Cards taken **without** a choice stay specific: Fiend Fire's whole hand, The Hermit's Curse, a whole-hand discard with a
    Sly card in it (`PlayFacts.MayDiscardSly`, which `CardEffects.DiscardsOnlyPicks` leaves out for picks: every discard
    follows a `Choice` step in the program).
  - A choice of **how many** (Neow's Fury, `HandTakeChosen`) keeps its `NotModelledAction`: the count changes what's predicted,
    not just which card.
  - The Play audit doesn't report what the chosen card itself caused: a Sly card you discarded playing itself (skipped as "the
    card you picked played itself (Sly)"), a picked Drum of Battle's energy. Each skip is counted under its own reason, the way
    a random roll is excused.

The compiler's own patterns are skipped: the `await` pattern (`GetAwaiter`, `get_IsCompleted`, the state switch), visual
and sound commands (`TriggerAnim`, `Vfx`, `Sfx`, `Preview`, `PlayMelodyVfx`, waits) and `ThrowIfNull`.

Control flow is read from the method's basic blocks, not line by line: the `await` pattern first, then loops (a jump back)
and branches (a conditional jump forward, with the commands on each side). A shape it doesn't recognize is `Unknown`.

The same reader reads an enchantment's and an affliction's `OnPlay`, and a potion's `OnUse` (drinking a potion is one of the
contract's decisions), into programs that use the same translators.

Calls into the card's own methods are read into the program, to a fixed depth. Calls into anything else that isn't a known
command become `Unknown`.

The reader works over a small instruction view, read from either source:
- in the game, Harmony's `CodeInstruction`;
- in the tests, `System.Reflection.Metadata` over each decompile's `assembly/sts2.dll` (decision #165, the way `CodeFacts`
  reads them).

So the catalog tests read every card on **both** branches.

Read from Iron Wave's state machine today (the spike):

```
25  CreatureCmd.GainBlock(Creature, Block, cardPlay)
63  DamageCmd.Attack(Damage.BaseValue) .FromCard .Targeting(Target) .Execute
```

Fiend Fire reads as a `foreach` with `CardCmd.Exhaust`, then `Attack.WithHitCount(<cardCount>)`. Sunder reads as an attack,
then `PlayerCmd.GainEnergy` behind a branch on its results.

### A2. Translating a step

`Sim/Adapter/ProgramTranslator.cs` turns each step into simulator actions, in program order. Its values come from today's
readers (`AttackOf`, `BlockOf`, `PowersOf`, `OrbsOf`, `CardEnergy`, `SimReaders`: captured terms, calculated values), changed
to take a step instead of a card.

The card events a command causes come from that command's step, not appended at the end:
- an `Exhaust` step is the card leaving the hand, then `Exhausted`;
- a `Draw` step is `Drawn` (or the drawn-card readers' `DrawnCardAction`);
- an `AddGeneratedCardToCombat` step is `Generated`.

Commands, by how many beta cards call them:

| Command | Cards | Today's action |
|---|---|---|
| `PowerCmd.Apply<T>` | 291 | `ApplyPowerAction` |
| `DamageCmd.Attack` | 237 | `AttackAction` (shots: `ShootAction`/`ImpaleAction`) |
| `CreatureCmd.GainBlock` | 93 | `GainBlockAction` |
| `CardPileCmd.Draw` | 61 | `CardEventAction(Drawn)` / `DrawnCardAction` |
| `CardPileCmd.AddGeneratedCard(s)ToCombat` | 45 | `CardEventAction(Generated...)` |
| `PlayerCmd.GainEnergy` | 27 | `CardEventAction(EnergyGained)` / kill reward |
| `OrbCmd.Channel`, `EvokeNext`, `AddSlots` | 30 | `OrbAction` |
| `CardCmd.Upgrade`, `CardPileCmd.Add`, `CardSelectCmd.*` | 60+ | hand changes, pick models, or not modelled |
| `CardCmd.Exhaust`, `Discard`, `DiscardAndDraw` | 28 | `LeaveHandAction` + card events |
| `CreatureCmd.Damage`, `Kill`, `LoseHp` (self) | 14+ | `DamageAction`, `ExecuteAction`, `LoseHpAction` |
| `ForgeCmd`, `OstyCmd`, `PlayerCmd.GainStars`, `CardCmd.AutoPlay`, `Transform` | 40 | their actions today |

Each translation is written once, and every card, enchantment and potion that calls the command goes through it. Relics' and
powers' models already call the same engine functions for the same commands (`SimEngine.ApplyPower`, the block and damage
pipelines), so a command's pipeline is one piece of code wherever it is used. The models themselves stay written by hand,
checked against their game hooks by Part B. Reading their hooks as programs too is the natural next step once this works;
it isn't part of this plan.

There are 58 commands on beta and 49 on main. A command with no translation becomes `NotModelledAction("<Command> in
<Card>.OnPlay")`. It is counted like every `SimState.Unmodelled` reason, and each one is a tracker item. It is never guessed.

**Built first (2026-11-23): the order.** `PlayOrder` gives each action the command it stands for (`KeyOf`) and each program
command the actions it can take (`KeysOf`). It then puts the adapter's actions in the program's order:
- a loop doing two or more things per round is split into rounds;
- an action no command stands for keeps its place after the one it followed.

The values still come from today's builders, which already read the code where they can (decision #157) and are tested. They
move to the program's arguments one table at a time; that is the next step, tracked in TRACKER.

A command a card calls that has no action yet adds no `NotModelledAction` in this first step. A new `?` on screen is the
owner's call, so those commands are listed, not shown.

### A3. Order from the program, nothing else

`SimAdapter.Play` builds the body by walking the program:
- `Repeat` repeats its steps;
- a known `If` becomes a branch or a conditional action (a kill reward, as today);
- an `Unknown` `If` makes its steps uncertain (not modelled, with the card and the condition).

What happens around the play is unchanged: energy and stars spent before it, its own exhaust after it, `CardResolvedAction`,
the play count, the enchantment's and affliction's `OnPlay` after the card's (also read as programs), and the Before and After
hooks.

**Removed:**
- the fixed order;
- the check that put Fiend Fire's exhausts before its attack;
- `DiscardThenDrawCards`, `DrawThenDiscardCards`, `ExhaustThenDrawCards`, `DrawThenExhaustCards` and the takes-first logic;
- every per-card table whose subject the program now says (each table's scan in `Diagnostics/CardCoverage.cs` goes with it). Done
  so far (`CardEffects`): the tables for Osty killed, a target's HP set, a target's block taken, damage without an attack, block
  for another or each player, a channel for the target's player, and a second attack on every enemy after a kill. Each scan
  now asks the same rule, so a card calling the command some other way is still flagged.
  - Draws and shuffles (2026-11-23): a draw for the target's player (`DrawsForTarget`: the draw's player argument isn't you),
    a draw for each teammate a loop goes over (`DrawsForEachTeammate`), a shuffle on every play (`ShufflesDiscardPileIn`: no
    condition around it) or only on a condition (`ShufflesWhenShort`). They replaced `AllyDrawCards`, `TeammatesDrawCards`,
    `ShuffleCards` and `ShuffleWhenShortCards`.
  - Cards moved between piles (2026-11-23): each `CardPileCmd.Add` is read by its pile argument and the pick before it (a
    `Choice` step now keeps its arguments, so whose pile it picks from is read): into your hand (`TakesIntoHand`), back onto
    your draw pile after a pick from your hand (`PutsBackFromHand`), or elsewhere after a pick (`MovesCardsElsewhere`: your
    discard pile onto your draw pile, a teammate's draw pile into their hand). They replaced `TakenToHandCards`,
    `PutBackCards` and `MovedElsewhereCards`.
  - Made cards (2026-11-23): the reader records each card the code makes (`CreateStep`: `CombatState.CreateCard<T>`, by
    type), so `MakesSadness` replaced `SadnessCards` (Mourning Blow turning into one included); `Forges` (`ForgeCmd.Forge`)
    replaced `ForgeCards`.
  - Attacks (2026-11-23): an attack keeps where `WithHitCount` gets its count (`CommandStep.HitCount`), and an argument read
    from a local stored once is described by what was stored (Whirlwind's `int x = ResolveEnergyXValue()`). `HitsXTimes`
    replaced `XHitCards`; `AttacksOnceRepeatingSomethingElse` (an attack with no hit count, and a loop over `Repeat` with no
    attack in it: Ice Lance, Heirloom Hammer) replaced `SingleHitRepeatCards`.
  - The rest (2026-11-23): `AutoPlaysFromDrawPile` (`PlaysFromDrawPile`), `SummonsEachPlayer` (an Osty summon for each player a
    loop goes over: `AllPlayersSummonCards`), `GainsStarsOnKill` (a star gain behind a test after its attack: `KillStarCards`),
    `DislodgesArrows` (`ArrowCmd.DislodgeArrow` on the target: Fool's `DislodgeCards`), `KeepsStretchForReplays` (Stretch to you
    after your block: `KeepsStretchCards`).
  - The whole hand (2026-11-23): a foreach now says what it goes over (`loop over each of Hand.Cards.ToList`), a pile is named by
    its type (`PileType.Hand.GetPile(owner)` reads as `Hand`), a query over a collection by what it queries (`Hand.Cards.ToList`),
    and a local kept across awaits and stored once by what was stored (The Devil's `cards`). `TakesWholeHand` (a loop over your
    hand's cards that exhausts, discards or moves each, or a discard of the hand's cards) replaced `WholeHandCards`.
  - Draws on a condition (2026-11-23, with conditions as values): `PlayFacts.CardsDrawn` reads every draw for you in the program
    (`CardEffects.DrawsForYou`: `CardPileCmd.Draw` to its owner, `CardCmd.DiscardAndDraw`), each with the condition it runs on and
    the loops around it, evaluated against the board by `ProgramEvaluator`, its count from its own argument (a card value, Scrawl's
    room in your hand, Calculated Gamble's hand, a calculated value). `DrawsOnACondition` and `DrawsDependOnTarget` (the condition
    asks `cardPlay.Target`) replaced `ConditionalDrawCards` and `TargetDrawCards`, and with them `CardsDrawn`'s cases for FTL,
    Impatience, Fetch, Restlessness, Ki Hop, Wrest, Scrawl, Calculated Gamble, a calculated count and Mad Science's Wisdom rider.
    The card scan flags a draw on a condition the evaluator can't read, in place of a gold-glowing card missing from the table.
  - Other cards exhausted (2026-11-23): `CardEffects.ExhaustsOfOthers` reads each `CardCmd.Exhaust` of other cards as a pick
    (`Picked`: the card a `CardSelectCmd` gave, or a loop over its result; from your draw pile when the pick names it; with a test on
    the picked card, The Hermit's Curse), a roll (`Random`: `NextItem` over a collection, never called) or a loop over a collection
    (`Each`: your hand, a `Where` of it, a helper's value such as Second Wind's `GetCards`). `PlayFacts.ExhaustOthers` counts them on
    the board (a pick one card a play or the selector's most, a roll one a play, a loop each card once), and `ExhaustPickOf` takes
    the pick, a filter by type as its `Only` or `Except`. They replaced `ExhaustOthers`' cases, `ExhaustPickOf`'s and
    `ExhaustTakesNoActingCard`: the card scan now evaluates a filter that isn't a type (Growth's Sadness, Florescence's Shoot cards,
    Flak Cannon's statuses) on every card that acts when exhausted, and flags one any could pass. The reader now also reads a
    helper's returned value, `is`, a struct built in place (a selector's prefs) and what a foreach goes over (`LoopStep.Items`).
  - **The evaluator** (`ProgramEvaluator`) evaluates a condition before the play, each query translated once: a property of the
    game's objects is read (the game's own answer), a method only when it only asks (`Is`, `Has`, `Can`, `Get`, `Contains`,
    `HappenedThisTurn`), never anything about the RNG; Linq over a collection with the code's own lambda; your hand without the
    played card (it's in play while its `OnPlay` runs); `Osty.CheckMissingWithAnim` as Osty missing. On a replay's later plays,
    the earlier plays have finished (a query over `CardPlaysFinished` counts them, through the card's own property read from its
    getter: FTL, Fetch), a test a loop ran until it failed stays failed (Wrest), and an arrow test follows your hits (Ki Hop:
    `SimEngine.PlaysStartingImpaled`). A test it can't evaluate counts as holding (the old tables' practice on the game's template
    card), and the card scan holds every draw's condition to being readable.
  - Each was proven to give exactly its table's cards on both branches before the table was deleted
    (`CardEffectsTests.EachRuleFindsExactlyTheTableItReplaced` keeps the tables as they were), and the catalog snapshot was
    unchanged by the swap.

A table stays only for what code can't say. Each one keeps its scan and gets a line in this doc saying why.

`PlayFacts` keeps the counts that tabs and checks read (draws, exhausts, energy), worked out from the program where it can be.
One source for both the tabs and the simulator.

### A4. Checks

- **Behaviour tests, written first** (`tests/SpireQoL.Catalog.Tests/PlayBehaviourTests.cs`). End to end: a real card built
  from the game's objects (`GameFight`), through the adapter and the simulator, with the outcome asserted (HP, block, powers,
  hand, piles, energy, the tabs' sources). They test what happens, not how the code is built, so they survive this rewrite
  and the next one.
  - They are written against today's code before anything changes: a case that is right today must stay right, and a case
    that is wrong today (Iron Wave into Thorns) is written to fail, then passes at the switch.
  - The documented cases:
    - Iron Wave and Dash into Thorns: the block takes the Thorns hit, and the crossed Thorns tab still shows. The tab means
      the attack sets Thorns off, which uses up your block even when no HP is lost.
    - Fiend Fire with Charon's Ashes (the exhausts' hits land before its kill), and with Dark Embrace (the draws stay).
    - Vicious and Dark Embrace drawing their amount.
    - Battle Trance drawing before its No Draw.
    - Sunder's energy only on a kill.
    - A double-played Iron Wave with Kunai at 2 (more block on the second play).
    - Tuning Fork at 9 on Acrobatics, Ki Chant and Burning Pact (fires; only the pick is uncertain).
    - Pillage's draws, Whirlwind's X, Second Wind with Feel No Pain and Juggernaut, Growth's Stretch.
    - The TRACKER's and DECISIONS' other play-order and play-count cases.
  - The rule from now on: a bug in a card's play gets a behaviour test here, besides its simulator test, so later rewrites
    keep it.
- **Catalog snapshot** (`CardPlaySnapshotTests`, `tests/SpireQoL.Catalog.Tests/Snapshots/card-plays.txt`): every card in the
  installed game on five boards built from the game's objects (one enemy with a Strike, a Defend and a Dazed beside the card,
  six cards to draw and three in the discard pile; the same upgraded; two enemies, a full hand and an empty draw pile; the first
  board with the card played twice, as Echo Form or Burst gives; the first board with a teammate in the fight, a card aimed at an
  ally aimed at them), with
  every `PlayFacts` field and the adapter's play (`SimAdapter.Play`) written as text. Moving a table onto the program must leave
  it unchanged; a difference that is a real fix regenerates it (`SPIREQOL_UPDATE_SNAPSHOT=1`), named in the commit with the card
  and why. A game update regenerates it too.
  - **Outcomes** (`Snapshots/card-outcomes.txt`, 2026-11-23): what each play results in, on the same boards and a sixth, the card
    alone in hand (so a condition on your hand goes the other way: Impatience draws, Restlessness draws and gains energy). The play
    is run through the simulator (`SimEngine.ApplyPlayTo`), and each branch it ends in is written with its weight, your HP, block,
    energy, hand and piles, each teammate's, the enemies' HP and block, and the play's card events (a replay's last play's). A
    change to a play's result shows here even when its actions read the same.
- **Program tests** (`tests/SpireQoL.Catalog.Tests/CardProgramTests.cs`): known programs on both branches' assemblies (Iron
  Wave, Fiend Fire, Sunder, Pillage, Whirlwind, Battle Trance, Growth, Second Wind, Blossom Shot's local function, an X-cost
  card, a selection card), and the shapes of conditions: Impatience's negated lambda over your hand, Ki Hop's local stored before
  its attack, Wrest's `while` inside its `if`, Restlessness' early return, Escape Plan's `&&`, Fetch's Osty check and own
  property, Pillage's round test; `ConditionsRejoinWhereTheWaysMeet` holds the simplification.
- **Coverage** (`CardProgramCoverageTests`): every card on both branches is read fully, or partly with its reasons, and the
  counts are kept in the test. A card that gets worse fails it; a card that gets better updates the counts. The same counts
  go into the coverage report.
- **Shadow comparison** (`CardProgramShadowTests`, during the build-out, then deleted): for every card in a `GameFight`, the
  old script and the new one are compared as action lists. Each difference is either an old bug (expected: the five order
  cards) or a translator gap. The switch happens when only reviewed differences remain.
- **Golden fights**: `SinglePlayerGoldenTests` is regenerated once, in the commit that switches, with the expected changes
  named (block before attack, events at their command). The replay cases and performance tests must pass unchanged, or with
  a stated reason.

### A5. What code can't say

The aim is every card read by the interpreter, with no card named. What is left falls in two groups.

**Can't, by the contract (the player's choice decides it).** These stay out on purpose, each with its scan:
- **Which card a pick takes** (A1, `Choice`): a pick is a card, never a particular one. What only a particular card would do
  (a Sly card playing itself, Drum of Battle's energy, the card's cost) is the player's own decision, neither simulated nor
  marked.
- **How many a pick takes** (Neow's Fury: `HandTakeChosen`): the count changes what's predicted, so it keeps its
  `NotModelledAction`.

**Not yet read (reader gaps, not limits).** Each still has its table and its scan, and what the reader needs to read it. Where
it stands (2026-11-23, after conditions as values): five more tables read from the program (`ConditionalDrawCards`,
`TargetDrawCards`, `ExhaustOthers`, `ExhaustPickOf`, `ExhaustTakesNoActingCard`), with `CardsDrawn`'s ten cases; card-name
references (`nameof`, `case`, `is` of a card class) in `Procs/` and `Sim/Adapter/` went from 344 to 286.

| Table (where) | Cards | What the program doesn't say yet |
|---|---|---|
| `DrawnCardReaders` (SimAdapter) | Pillage, Scrape and others acting on what they draw | The tests are read now (Pillage's loop's `While`: the drawn card, its type, your hand's room; Escape Plan's block on the drawn card being a Skill); what's left is turning a test of a command's result into the simulator's `DrawnCardAction`. |
| (no table) a replay's later plays and your hand | Restlessness, Impatience, Scrawl replayed | A condition on your hand is evaluated on the hand before the play for every play of a replay, as before: the cards the first play draws aren't in it yet (the simulator knows them; the evaluator doesn't). |
| `DiscardCount`, `TransformCount`, `TakenToHandCount`, `GeneratedCount` (PlayFacts) | most cards that discard, transform, take or make cards | Within reach now: the exhausts show the way (a pick's size from its selector, a loop's collection evaluated with its lambda). Not done yet. |
| `ChannelCount`, `RepeatedBlockCards`, `BlockGainCount`'s cases, `HitsPerPlay`'s cases | orb, block and multi-hit cards | Counts from loops and conditions (`loop over Repeat`, Chill's loop over enemies, Cho Firm's arrow test). Both are read now (a loop's `While`: `i < 2`, `i < blockGains`; conditions evaluate); what's left is a count rule per command (channels, block gains, hits) built on them. |
| `SplashCards`, `OverkillReaders`, `RepeatPerKillCards` | Rain Shot, Omnislice, Fisticuffs, Blossom Shot, Echoing Slash | What a hit's result feeds (`TotalDamage + OverkillDamage` into the next damage, a kill into the loop's count): needs arithmetic over a command's result. |
| `TriggerPoisonCards`, `TeammatesShivCards`, `EnemyStrengthCards` | Outbreak, Blade Symphony, Strength takers | A call on a power (`PoisonPower.Trigger` isn't a command); the player a helper's Shivs go to (an argument inside the helper); a power's sign and duration. |
| `GlowGatedAttackCards`, `GlowNotTheAttack` | Pact's End; Fetch, Flatten, Nail | Whether the card's glow (`ShouldGlowGoldInternal`) is its attack's condition. Both can be read now (`CardProgramReader.ReadProperty` reads the glow's getter; the attack's condition is a value), so the attack's condition can simply be evaluated instead: the tables can go with `HitsPerPlay`'s cases. |
| `HandTakes`, `AutoPlayFromDrawAction`'s count, `Fool.BlockWithoutVar` | the take-into-hand cards, Havoc, Cascade, The Devil | Which pile and filter a take uses, and the count argument (`cards.Count`, X + 1 upgraded). |

## Part B: pipelines and models checked against the game's code

- **B1. Pipeline steps.** Each simulator command declares the game command it mirrors and its hook calls in order (for
  example `GainBlock: BeforeBlockGained, ModifyBlock, AfterModifyingBlockAmount, AfterBlockGained`). `PipelineParityTests`
  reads each game command method's `Hook.*` calls, in order, on both branches, and fails on a difference. That would have
  caught `ShouldDraw`.
- **B2. Model amounts.** `ModelEffectCheck` also reads where each command's amount comes from in a model's game hook
  (`base.Amount`, a value, a constant). It fails when the model takes it from somewhere else. That would have caught Vicious
  and Dark Embrace.
- **B3. Listeners beyond powers and relics** (after A and B1–B2, in the same push if time allows): the listener list follows
  `IterateHookListeners` fully. That adds orbs (today `SimEngine.Orbs`), monsters (today `EnemyModels`), potions, and cards
  with their own hooks in your piles (38 beta cards override a hook). Each is added as a model in that order, with a test.

## Order of work (one session)

1. Baseline: `scripts/check.ps1` green. Write the behaviour tests (A4) against today's code: the right cases pass and the
   known-wrong ones fail.
2. Instruction view (Harmony and Metadata) and the program reader, with the program tests on both branches.
3. Coverage test and report.
4. Translators per command, with the old builders refactored to take steps.
5. Shadow comparison until only reviewed differences remain.
6. Switch the adapter: every behaviour test passes, including the known-wrong ones. Delete the fixed order and the tables,
   and regenerate the golden fights with reasons.
7. B1 pipeline parity, B2 model amounts, then B3 listeners.
8. Docs: SIMULATOR.md (Card scripts, The adapter), FEATURES (only the corrected predictions show), CHANGELOG under Spire
   QoL 2.0.0, TRACKER, DECISIONS.
9. Build and install, then the owner plays (the audits are the final check). Released as 2.0.0, not batched with 1.27.x.

## Risks

- **The compiler's shapes.** Async state machines, closures and local functions. The reader recognizes the patterns it knows
  and marks the rest `Unknown`: a wrong read becomes uncertainty, not a wrong certainty. The program tests run on both
  branches, so a compiler change shows up in tests.
- **Coverage on day one.** Of 677 beta cards, 443 are straight lists of commands. 162 have a condition (most are visual
  checks or a pick's null check), 112 a loop and 68 a choice (they overlap).
  - A card whose program isn't fully read yet keeps today's script, so nothing that is exact today becomes a `?`.
  - The coverage test counts those cards. The aim is none left by the end of the session; any left over are tracker items,
    and today's builder is deleted with the last one.
- **Speed.** A program is read once per card type. Translation runs per play, as the script builder does now. The
  performance tests hold the line.
- **Size.** Steps 1–6 are the core and fit one session. Most of the time goes into the shadow comparison over 677 cards.
  B1 and B2 are small. B3 (orbs, monsters, potions and cards as listeners) may spill into a second session; it doesn't block
  the release.
- **Hidden information.** Unchanged: the reader reads code, never the RNG, the seed or the draw order.

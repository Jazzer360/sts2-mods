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
  - **A value two ways leave** (2026-11-23, round three): where ways meet with a value on the stack (`bool flag = a && b`: the jump past
    `b` leaves false; `x = c ? 2 : 1`; `target?.GetPowerAmount()`), the value is each way's on its condition (`ConditionValue` for a
    true-or-false, else `WaysValue`: the first way whose condition holds), and a local stored on several ways is its last store's on
    each way (AllForOne's `Filter`; Evil Eye's `blockGains`). Before, one way's value was read for all of them (AllForOne's filter
    read as false). A store in a loop with the read (a loop's counter) isn't read.
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
  - Cards taken (2026-11-23, round three): one rule reads every command that takes cards (`CardEffects.Takes`, behind
    `ExhaustsOfOthers`, `Discards`, `Transforms` and `TakesIntoHandOf`) by what its cards argument is: a pick's card or cards (with the
    pick's pile, its filter and its selector: how many, and whether you choose how many: Guards, Neow's Fury), a roll (`NextItem`,
    `TakeRandom` with its count), each card of a collection (a loop's, or one given whole: your hand, a `Where`, a list the play
    filled in a loop, the first of one), the cards a draw gave (Scrape) or the top of your draw pile looked at, less the ones picked
    (Ki Chant's `Except(Take(drawPile, count), picked)`). `PlayFacts.Taken` counts them on the board (a pick from your hand has the
    cards the play drew before it; one from your draw pile the discard pile its play shuffles in first; a transform only what
    `CardCmd.Transform` can take). They replaced `DiscardCount`, `TransformCount`, `TakenToHandCount` and the adapter's `HandTakes` table
    (`SimAdapter.HandTakeOf`: which piles, which cards, a roll or a pick, one a play or all), and the name checks on Guards, Neow's Fury, Ki
    Chant, Scrape and The Hermit with them (a pick shared by an exhaust and a discard: `SharedPickSize`). Fix: The Hermit with only
    Curses in hand no longer counts a discard.
  - Orbs (2026-11-23, round three): `PlayFacts.OrbsOf` reads what a play does to your orbs from its program
    (`CardEffects.OrbChannels`, `OrbEvokes`, `OrbSlotChanges`, all found with `CardEffects.Find`: a command with its condition and
    loops): each channel's orb (its class, or random: a `GetRandomOrb` orb) and its loops' rounds on the board (`PlayFacts.Rounds`:
    a foreach's collection, a counter's bound, the card value it's over), the evokes of your front orb or of every orb (a loop over
    your orbs: Shatter), the slots added or removed. An X is the card's X (`ResolveEnergyXValue`: what playing it now spends, through
    the X hooks). They replaced `SimAdapter.Channels`, `RepeatChannels`, `SlotChange`, `ChannelKind`, `HandlesChannel`, the evoke cards'
    switch in `OrbsOf` and `EvokedOrbs`, and `PlayFacts.ChannelCount`'s cases. Fix: upgraded Tempest's extra Lightning is counted for
    Metronome (its channels already were).
  - Block gains (2026-11-23, round three): `PlayFacts.BlockGainCount` counts each `CreatureCmd.GainBlock` of the play
    (`CardEffects.BlockGains`) on its condition, times its loops' rounds (Second Wind, Cho Limbs, Evil Eye, Death's Door, Cho Firm;
    Rally's loop over every player gains yours once), none when the game's `GainsBlock` says none. `RepeatedBlockCards`,
    `BlockGainCount`'s cases and Fool's `BlockWithoutVar` (the props are the gain's own argument) are gone; the card scan holds a gain in
    a loop to its rounds being readable (`CardEffects.RoundsReadable`). Fix: Bone Shards and Sacrifice with Osty missing gain no block
    (their code returns first; the game's flag doesn't say so).
  - Hits (2026-11-23, round three): `PlayFacts.HitsPerPlay` reads the play's attack (`CardEffects.Attacks`, the first): its condition
    evaluated on the board (Pact's End's `CanDealDamage`, Nail's Impaled target, Fetch's and Flatten's Osty), its hit count's value
    (`CommandStep.HitCountValue`, `WithHitCount`'s argument: a card value on beta and a constant on main, a card's X, Heavenly Drill's
    doubled X, Dismantle's Vulnerable target, Spite's HP lost this turn, Wah Blast's melodies, Fiend Fire's hand, a calculated count)
    and its loops' rounds. With no target, a test of the target holds when it does for some enemy and a count that asks the target is its
    least. It replaced `HitsPerPlay`'s eleven cases, `GlowGatedAttackCards`, `GlowNotTheAttack`, `AttacksConditionallyAndGlows` and
    the branch rule `AttackHitCountsAreVars` (main's fixed 2 is read from main's code). The card scan holds the attack's condition and
    hit count to being readable.
  - What a card does with what it draws, and auto-plays (2026-11-23, round three): `SimAdapter.DrawnCardReaderOf` reads a test of
    the result of the card's `CardPileCmd.Draw` on a later step into the simulator's `DrawnCardAction`: a loop going round while the
    drawn card is of a type, drawing again (Pillage), a block only when it's of a type (Escape Plan), a discard of the drawn cards a
    filter on their cost keeps (Scrape). It replaced the `DrawnCardReaders` table and `UseDrawnCard`'s cases; DrawScanTests holds every
    card that uses what it draws to one of these. `SimAdapter.AutoPlaysOf` reads `CardPileCmd.AutoPlayFromDrawPile`'s count, its
    exhaust argument and the upgrading callback it's given (Havoc, Cascade's X, The Devil's hand, upgraded), replacing those cards'
    names in the adapter.
  - Enemies' Strength (2026-11-23, round three): `SimAdapter.EnemyStrengthOf` reads each `PowerCmd.Apply` of `StrengthPower` or of
    a temporary Strength power (`TemporaryStrengthPower`, its sign from whether it's a debuff) to the enemy the card is aimed at or to
    every enemy you can hit (a loop over them or all at once), its amount evaluated (decimal's operators and constants now evaluate:
    Shared Fate's `-EnemyStrengthLoss`, Resonance's `Decimal.MinusOne`). It replaced `EnemyStrengthCards`; StrengthLossTests proves
    the cards and amounts are the table's.
  - A power's own action the card runs, awaited (`CardProgramReader.IsPowerAction`: Outbreak's `PoisonPower.Trigger` on each enemy's
    poison) is a step like a command; `CardEffects.TriggersPoison` replaced `TriggerPoisonCards` (only Outbreak has one, on both branches).
  - **One play, repeated** (2026-11-23, decision #189): the adapter describes one play from the program and the simulator's loop repeats
    it, as `CardModel.OnPlayWrapper` does; the card goes to its pile once, after the last play. What a play takes from your hand is a
    `TakeFromHandAction` from its `CardsTaken` (every card of the collection there now, a pick's or a roll's most, a filtered take's cards
    followed through the play), decided by the simulator as it runs; a value that counts what a take took (`CardEffects.CountsTaken`: Fiend
    Fire's hit count, Calculated Gamble's draws, The Devil's auto-plays) or your hand's room (`CardEffects.ReadsHandRoom`: Scrawl, The World)
    is read there too (`SimCount`). `PlayFacts` holds one play's counts; nothing is multiplied or shared out by the play count
    (`PerPlayRuleTests`).
  - **The evaluator** (`ProgramEvaluator`) evaluates a condition before the play, each query translated once: a property of the
    game's objects is read (the game's own answer), a method only when it only asks (`Is`, `Has`, `Can`, `Get`, `Contains`,
    `HappenedThisTurn`), never anything about the RNG; Linq over a collection with the code's own lambda; your hand without the
    played card (it's in play while its `OnPlay` runs); `Osty.CheckMissingWithAnim` as Osty missing. On a replay's later plays,
    the earlier plays have finished (a query over `CardPlaysFinished` counts them, through the card's own property read from its
    getter: FTL, Fetch), a test a loop ran until it failed stays failed (Wrest), and an arrow test follows your hits (Ki Hop:
    `SimEngine.PlaysStartingImpaled`). A test it can't evaluate counts as holding (the old tables' practice on the game's template
    card), and the card scan holds every draw's condition to being readable. It answers what the tabs ask ("right now"); the simulation
    asks its own conditions (next).
  - **Conditions apart from effects** (2026-11-23, decision #190): a step on a condition is the ordinary action for its command (a
    draw, a gain of energy or stars, an attack, a block, Osty's death) inside a `WhenAction`, guarded by a `SimCondition` the engine asks
    of each branch as it runs (`Sim/SimConditions.cs`). `ConditionReader` translates each test of the code once, for every card: an
    attack's results' `WasTargetKilled` (the last attack killed), `Osty.CheckMissingWithAnim` and `IsOstyAlive`, the arrow tests on the
    target, a count or an `Any` of your hand's cards (less the card itself; with a lambda, a filter followed through the play as a
    filtered take's is), of `CardPlaysFinished` this turn by you or of the card, and a property of the card read from its getter. The
    card's own values (a card value, its upgrade, a field it keeps: Mad Science's rider) and a pick's card (#188) are decided before
    the play. A test the code asks once is asked once a play (`SimCondition.At`: an else, a later step under the same if), and a local
    stored before the first command is asked there (`AskAction`: Ki Hop, Bow Smack). `PlayConditions` guards the draws, the conditional
    gains of energy and stars, Timber's later attack and every action the facts build (by its command's step from `PlayOrder`). They
    replaced the attack's kill rewards (Sunder's energy, Knockout Blow's stars) and Timber's kill attack inside its hit, a draw's count
    for each play index worked out before the play (FTL, Fetch, Wrest, Ki Hop at the worst target), and Sunder's, Bow Smack's and
    Restlessness' energy rules. Catalog `ConditionTranslationTests` hold each to its code on both
    branches.
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
it stands (2026-11-23, after round three): the count tables (`DiscardCount`, `TransformCount`, `TakenToHandCount`, `HandTakes`,
`ChannelCount` with `Channels`, `RepeatChannels` and `SlotChange`, `RepeatedBlockCards`, `HitsPerPlay`'s cases), `DrawnCardReaders`,
`EnemyStrengthCards`, `TriggerPoisonCards`, the glow tables, Fool's `BlockWithoutVar` and the auto-plays' counts read from the program;
card-name references (`nameof`, `case`, `is` of a card class) in `Procs/` and `Sim/Adapter/` went from 286 to 106 (344 before
round two). What's left, by file: `PlayFacts` 62 (`GeneratedCount` and the forged blade 57, the splash and overkill tables 4, Thinking Ahead 1), `CardEnergy` 23, `SimAdapter` 8, `ReturnToHand` 5,
`SimAdapter.Play` 4, `BranchRules` 4.

| Table (where) | Cards | What the program doesn't say yet |
|---|---|---|
| (no table) a replay's later plays: conditions with no simulator test yet | Pact's End, Forgotten Ritual, Bubble Bubble, Molten Fist, Go for the Eyes, Memento Mori | Since decision #190 a step on a condition is its ordinary action under a simulator condition (`WhenAction`), asked on each play as it runs: Osty (Sacrifice, Bone Shards, Osty's attacks), your hand's cards and their kind (Impatience, Restlessness, Wheel of Fortune), plays finished this turn (FTL, Fetch), the target's arrows (Ki Hop, Bow Smack, Wrest, Nail, Recall, Uproot), the last attack's kill (Sunder, Knockout Blow, Timber). What has no simulator test yet (the exhaust pile, an exhaust this turn, a target's power the simulator doesn't keep, the enemies' count) is decided on the board before the play, listed by the card scan. |
| (no table) Flak Cannon's hits on a replay | Flak Cannon | Its hit count is its `CalculatedHits` var (the statuses it will exhaust), a calculated var's lambda the reader doesn't follow, so it isn't recognized as the count of what its take took. |
| `GeneratedCount` (PlayFacts) | cards that make cards | Within reach: `CardPileCmd.AddGeneratedCard(s)ToCombat`'s cards (a `CreateStep`, a loop's rounds, a helper's count: Shiv.CreateInHand) and their pile. Not done yet. |
| `SplashCards`, `OverkillReaders`, `RepeatPerKillCards` | Rain Shot, Omnislice, Fisticuffs, Blossom Shot, Echoing Slash | What a hit's result feeds (`TotalDamage + OverkillDamage` into the next damage, a kill into the loop's count): needs arithmetic over a command's result. |
| `TeammatesShivCards` | Blade Symphony | The player a helper's Shivs go to (an argument inside the helper: a game helper's parameters aren't bound to its caller's arguments yet). |
| `CardEnergy.Rules` (Sim/Adapter) | Forgotten Ritual, Double Energy, Mad Science, Epic Retrieval, Believe in You, Constellation, Energy Surge, Drum of Battle | Sunder, Bow Smack and Restlessness left it (decision #190: their gain is guarded by its condition, `CardEnergy.OnACondition`). What's left: an exhaust this turn (no simulator test yet), "as much as you have" (Double Energy), the rider (Mad Science, decided before the play), and whose energy it is. |
| `BlockOf`'s cases (SimAdapter.Play) | Fisticuffs, Entrench, Mimic | The gain's amount argument: the attack's result (Fisticuffs, with Blossom Shot's Stretch: arithmetic over a command's result), your block (Entrench), the teammate's (Mimic). |
| `PutBack`'s count (PlayFacts) | Thinking Ahead | The put-back pick's selector, as the discards' picks are read. |
| Cards' own hooks, not plays | Make It So, Right Hand Hand, Whistling Arrows, Bolas, Bounce, Thrumming Hatchet (`ReturnToHand`); Howl from Beyond, I Am Invincible (end-of-turn auto-plays); Kingly Kick, Kingly Punch (their cost on draw) | A card's hooks (After*, Before*) aren't its play: reading them as programs is Part B3's next step. Each is reviewed by its scan. |
| `BranchRules`' facts | Brightest Flame, Omnislice | Not a table: the code facts a branch rule is told by (decision #169) name the class their code mentions. They go when main catches up. |

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

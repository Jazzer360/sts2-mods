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

`Procs/Programs/ModelProgram.cs` (`ProgramReader`) reads a card type's play into a `ModelProgram`, once per type and cached (`CardPrograms`; any other game method: Part C). It reads the loaded game's
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

`Sim/Adapter/SimAdapter.Steps.cs` turns each command step into simulator actions, in program order (decision #193, 2026-11-23, round
four). A translator per command kind (`SimAdapter.Translate`: `CommandsTranslated` names each) builds the action from the step's own
arguments: an attack's damage value (the card value its first argument names), hits (`WithHitCount`'s argument at the target, times its
loops' rounds, through the game's hit-count hooks) and targeting and dealer (its builder: `TargetingAllOpponents`, `FromOsty`); a block's
gainer (you, the teammate it's aimed at, each player of your side a loop goes over) and value; a power's target (you, the target or the
teammate it's aimed at, every enemy, a roll's enemy, every player of your side) and amount (its third argument; the target's amount of a
power as it lands: Molten Fist, Dominate); a draw's player and count; an energy gain's player and amount (your own energy: Double Energy);
an orb command's orb, player, rounds and dequeue; each take (exhaust, discard, transform, into your hand, back onto your draw pile, your
whole hand) as a `TakeFromHandAction` or `TakeToHandAction`; a shot's arrow, the attack it's given and its impale; an auto-play's count,
exhaust and upgrading callback; a shuffle and the draw pile count its condition tests. Where an amount can only come from the game's own
math on the board (damage and block terms through the pipelines, B1), those terms are still read; never whether the step happens.

The step's condition is the action's (decision #190, `SimAdapter.Guard`): a `WhenAction` when the simulator asks it, left out when it's
decided false on the card's own values, and decided on the board when it has no translation yet (a gap the card scan lists). A test of a
take's card is the take's filter; an evoke's test of your orbs and a shuffle's test of your draw pile are what those actions ask themselves.
A loop's body is walked once: its rounds are what its steps' actions count (hits, gains, channels, draws), and a take's loop runs its other
steps after each card (Second Wind's block). The play order and the guard put on the facts' actions after the fact are gone with the facts' gating: whether an attack, a
block, a power or a take happens is never decided on the board before the play.
What a step's arguments are (whom it acts on, which card value it names, a counter loop's bound) is read by `StepValues`, and what the program gives on
the board (a take's cards now, an attack's hits at its target, an auto-play's count) by `BoardCounts` (decision #194): the translators only build actions.

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

A command with no action yet is a named gap (`SimAdapter.CommandsNotTranslated`, with why: a card upgraded mid-play, Osty summoned, a
heal...), and `StepTranslationTests` holds every command every card calls, on both branches, to one or the other. A gap adds no
`NotModelledAction`: a new `?` on screen is the owner's call, so they're listed, not shown (TRACKER).

**Built first (2026-11-23): the order.** `PlayOrder` gave each action the command it stood for and put the adapter's actions in the
program's order, the values from the card-level builders. Replaced in round four by the actions built from the steps themselves (above).
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
    loop goes over: `AllPlayersSummonCards`), a star gain behind a test after its attack (`KillStarCards`; since decision #190 a gain guarded by its kill),
    `DislodgesArrows` (`ArrowCmd.DislodgeArrow` on the target: Fool's `DislodgeCards`), `KeepsStretchForReplays` (Stretch to you
    after your block: `KeepsStretchCards`).
  - The whole hand (2026-11-23): a foreach now says what it goes over (`loop over each of Hand.Cards.ToList`), a pile is named by
    its type (`PileType.Hand.GetPile(owner)` reads as `Hand`), a query over a collection by what it queries (`Hand.Cards.ToList`),
    and a local kept across awaits and stored once by what was stored (The Devil's `cards`). `TakesWholeHand` (a loop over your
    hand's cards that exhausts, discards or moves each, or a discard of the hand's cards) replaced `WholeHandCards`.
  - Draws on a condition (2026-11-23, with conditions as values): `PlayFacts.CardsDrawn` reads every draw for you in the program
    (`CardEffects.DrawsForYou`: `CardPileCmd.Draw` to its owner, `CardCmd.DiscardAndDraw`), each with the condition it runs on and
    the loops around it, evaluated against the board by `ProgramEvaluator`, its count from its own argument (a card value, Scrawl's
    room in your hand, Calculated Gamble's hand, a calculated value). Two rules on the draws (on a condition; the condition
    asks `cardPlay.Target`; since decision #190 each draw is guarded by its own condition) replaced `ConditionalDrawCards` and `TargetDrawCards`, and with them `CardsDrawn`'s cases for FTL,
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
    (Ki Chant's `Except(Take(drawPile, count), picked)`). `BoardCounts.Taken` counts them on the board (a pick from your hand has the
    cards the play drew before it; one from your draw pile the discard pile its play shuffles in first; a transform only what
    `CardCmd.Transform` can take). They replaced `DiscardCount`, `TransformCount`, `TakenToHandCount` and the adapter's `HandTakes` table
    (`SimAdapter.HandTakeOf`: which piles, which cards, a roll or a pick, one a play or all), and the name checks on Guards, Neow's Fury, Ki
    Chant, Scrape and The Hermit with them (a pick shared by an exhaust and a discard: `SharedPickSize`). Fix: The Hermit with only
    Curses in hand no longer counts a discard.
  - Orbs (2026-11-23, round three): `PlayFacts.OrbsOf` reads what a play does to your orbs from its program
    (`CardEffects.OrbChannels`, `OrbEvokes`, `OrbSlotChanges`, all found with `CardEffects.Find`: a command with its condition and
    loops): each channel's orb (its class, or random: a `GetRandomOrb` orb) and its loops' rounds on the board (`ProgramEvaluator.Rounds`:
    a foreach's collection, a counter's bound, the card value it's over), the evokes of your front orb or of every orb (a loop over
    your orbs: Shatter), the slots added or removed. An X is the card's X (`ResolveEnergyXValue`: what playing it now spends, through
    the X hooks). They replaced the adapter's `Channels`, `RepeatChannels`, `SlotChange`, `ChannelKind`, `HandlesChannel`, the evoke cards'
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
  - Enemies' Strength (2026-11-23, round three; since decision #193 each step's own action, `SimAdapter.PowersFrom`): the adapter reads each `PowerCmd.Apply` of `StrengthPower` or of
    a temporary Strength power (`TemporaryStrengthPower`, its sign from whether it's a debuff) to the enemy the card is aimed at or to
    every enemy you can hit (a loop over them or all at once), its amount evaluated (decimal's operators and constants now evaluate:
    Shared Fate's `-EnemyStrengthLoss`, Resonance's `Decimal.MinusOne`). It replaced `EnemyStrengthCards`; StrengthLossTests proves
    the cards and amounts are the table's.
  - A power's own action the card runs, awaited (`ProgramReader.IsPowerAction`: Outbreak's `PoisonPower.Trigger` on each enemy's
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
    target, your arrows (`PlayerHasAnyArrows`), a question to the fight's log (decision #191: the history ledger's count, Forgotten Ritual's
    exhaust this turn; Ki Rest's melodies through `MelodyCmd`'s helper, its tag bound from the call), a count or an `Any` of your hand's cards (less the card itself; with a lambda, a filter followed through the play as a
    filtered take's is), of `CardPlaysFinished` this turn by you or of the card, and a property of the card read from its getter. The
    card's own values (a card value, its upgrade, a field it keeps: Mad Science's rider) and a pick's card (#188) are decided before
    the play. A test the code asks once is asked once a play (`SimCondition.At`: an else, a later step under the same if), and a local
    stored before the first command is asked there (`AskAction`: Ki Hop, Bow Smack). `PlayConditions` guards the draws, the conditional
    gains of energy and stars, Timber's later attack and every action the facts built (by its command's step; since decision #193 each action is its step's own). They
    replaced the attack's kill rewards (Sunder's energy, Knockout Blow's stars) and Timber's kill attack inside its hit, a draw's count
    for each play index worked out before the play (FTL, Fetch, Wrest, Ki Hop at the worst target), and Sunder's, Bow Smack's and
    Restlessness' energy rules. Catalog `ConditionTranslationTests` hold each to its code on both
    branches.
  - Each was proven to give exactly its table's cards on both branches before the table was deleted
    (`CardEffectsTests.EachRuleFindsExactlyTheTableItReplaced` keeps the tables as they were), and the catalog snapshot was
    unchanged by the swap.

A table stays only for what code can't say. Each one keeps its scan and gets a line in this doc saying why.

Since decision #194 nothing about a card is decided before its play beside the simulator: the program's counts on the board are
`BoardCounts` and `MadeCards` (Programs: the step translators', the card scan's and the Shuffle Tag's), a card's prediction is
`CardPrediction`, and the counts the play check holds a play to are its simulation's log. One source for both the tabs and the checks.

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
  the adapter's play (`SimAdapter.Play`) written as text (until decision #194 every PlayFacts field beside it). Moving a table onto the program must leave
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
round two). Round five (decision #194) deleted PlayFacts and its 62 (`GeneratedCount` and the forged blade 57, the splash and overkill tables 4,
Thinking Ahead 1), with `SimAdapter`'s `RepeatPerKillCards` and `BlockFor`'s Fisticuffs, Entrench and Mimic. What's left is listed below.

| Table (where) | Cards | What the program doesn't say yet |
|---|---|---|
| (no table) a replay's later plays: conditions with no simulator test yet | Memento Mori | Since decision #190 a step on a condition is its ordinary action under a simulator condition (`WhenAction`), asked on each play as it runs: Osty (Sacrifice, Bone Shards, Osty's attacks), your hand's cards and their kind (Impatience, Restlessness, Wheel of Fortune), plays finished this turn (FTL, Fetch), the target's arrows (Ki Hop, Bow Smack, Wrest, Nail, Recall, Uproot), the last attack's kill (Sunder, Knockout Blow, Timber), the fight's log (decision #191: Forgotten Ritual's exhaust this turn, Ki Rest's melodies), your arrows (Arrow Stab), your exhaust pile (Pact's End), the target's powers (Bubble Bubble's poison, Molten Fist's Vulnerable, also its amount) and its intent to attack (Go for the Eyes). What has no simulator test yet (the enemies' count, your orbs, your draw pile's cards) is decided on the board before the play, listed by the card scan. |
| (no table) a made card's pile in a game helper | Shiv.CreateInHand's Shivs (Blade Dance, Storm of Steel, The Moon...) | Read since decision #194: every make is its step's (`MadeCards`: its count, card, owner, creator and pile; a game helper's parameters bound to what the call passes). What the reader can't read yet is the pile a helper passes after a `??` (`creator ?? owner` leaves the stack unread at the join): such a make goes where every pile the card's own code writes goes (`CardCode.MadeInto`), the hand for every card that calls the helper today. |
| (no table) a loop's count the code adds a command's kills to | Echoing Slash | Read since decision #194 from what the code shows (`CardEffects.RepeatsPerKill`: a loop while a count stays above 0 around a damage to every enemy, in a card whose code counts its results' kills); the reader doesn't keep the count's `+=` itself, so the rule reads the loop's shape, not its arithmetic. What a hit's result feeds otherwise is the command it's given to: the next damage (`CardEffects.SplashOf`: Rain Shot, Omnislice), a block gain (`CardEffects.BlockBaseOf`: Fisticuffs) or Stretch (Blossom Shot). |
| `CardEnergy.Rules` (Sim/Adapter) | Epic Retrieval, Drum of Battle | Their hooks, not their plays (AfterArrowDislodged, AfterCardExhausted). Every gain in a play is its step's (decision #193: `SimAdapter.EnergyFrom`): Double Energy's your own energy, Mad Science's rider decided before the play, Believe in You's and Constellation's teammate, Energy Surge's each player of your side. |
| (no table) damage that isn't an attack, from an attack card | Rain Shot, Omnislice, Echoing Slash | `CreatureCmd.Damage` on the target or every enemy is simulated as the card's attack (with the splash or the repeats its code shows: `CardEffects.SplashesDamage`, `RepeatsPerKill`), as before: the game runs no BeforeAttack or AfterAttack for it. |
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

## Part C: model programs (decision #195)

The reader reads any game method, not only a card's play: a monster's moves and hooks now, a power's or relic's hooks next. One reader
(`ProgramReader`), one program (`ModelProgram`: its `Owner` is the class it was read from), the same steps and conditions.

### C1. Reading any game method

- **The cache.** `ModelPrograms.Of(owner, method)` reads a method of any model class from the running game's code once and keeps it
  (decision #31: never per call). Cards keep their own cache (`CardPrograms`).
- **A monster's code** (`MonsterCode`, found by shape, never by a list): its moves are the methods its `MoveState`s perform (a `Task`
  taking the move's targets; in the game a move is reached from the `MoveState` that names it, its `_onPerform`), its hooks the game's
  virtual methods it overrides (`AfterAddedToRoom`, `AfterDeath`, `BeforeRemovedFromRoom`, `AfterDamageReceived`...), less how it looks
  and how its move rules are built.
- **What the reader adds for models** (each a general rule, tested on cards too):
  - a store to the model's own state is a step (`StoreStep`: a field or a property of its class or a base class of the game's, set to a
    value: Queen's `HasAmalgamDied`, Bowlbug Rock's `IsOffBalance`). It's what a later read of that state reads (a conditional move
    branch, the model's next hook), so it isn't dropped as a non-command any more;
  - a monster's call into its base class that changes it (`MonsterModel.SetMoveImmediate`: Queen's Enrage when her Amalgam dies) is a
    step like a command (`ProgramReader.IsModelAction`); End of Days' `OnDieToDoom` on a monster it kills is named for cards;
  - a call to the base class's own code of the hook being overridden (`base.AfterAddedToRoom()`, a non-dispatched call to a virtual
    method of a base class) is read as that code (MonsterModel's: nothing).
- **The scan** (`MonsterProgramTests`, catalog tests, both branches): every monster's moves and hooks are read with nothing the reader
  couldn't follow, and every command in them is translated into the enemy turn's action (`SimAdapter.MoveCommandsTranslated`) or is a
  named gap with why (`MoveCommandsNotTranslated`). At v0.113.1 (beta) and v0.111.1 (main): 116 monster classes with code, 318 moves
  and 93 hook bodies on each branch, every one read.

### C2. The enemy turn runs each move's program

`SimAdapter.MoveOf` translates the move an enemy is about to make (its `NextMove`'s `_onPerform`) into the enemy turn's actions
(`SimEnemy.Move`, docs/SIMULATOR.md "Enemy moves"):

| Command | Translated to |
|---|---|
| `DamageCmd.Attack` | `MoveAttackAction`: the intent's hits of the attack at its place (one unconditional attack step for each attack intent: every move in the game, `EnemyMoveTests`) |
| `PowerCmd.Apply<T>` | `MovePowerAction`: on each creature its target argument names, worked out on the board (`targets` the living players, `this.Creature`, `GetTeammatesOf`, a loop's items), its amount its third argument |
| `CreatureCmd.GainBlock` | `MoveBlockAction`: on each enemy its first argument names, through the game's block math when read |

Values are worked out by the program evaluator on a model's board (`ProgramBoard.Of(monster)`: "this" the monster, no card). The other
commands are named gaps (`MoveCommandsNotTranslated`); a power or block step on a condition or of a power made first is a reviewed gap
(`MoveStepGaps`, `EnemyMoveTests.ReviewedGaps`: none changes a later hit). A hidden intent keeps its move hidden.

### C3. Spawns and starting powers

`SimAdapter.SpawnsOf` reads each power's AfterDeath for the creatures it adds (`CreatureCmd.Add<T>`, or a creature made by
`CreateCreature(ModelDb.Monster<T>())`), on its condition evaluated as the creature dies now, each with every HP its class can start with
and the powers its AfterAddedToRoom gives it (`StartingPowers`). It replaced `SurpriseSpawns` (`SpawnTests`).

### C4. Conditional branches

`MoveConditions` reads each ConditionalBranch's lambda (`ProgramReader.ReadReturned`, a property through its getter) into a simulator
condition asked as that monster: `MonsterHp`, `MonsterPower`, `MonsterSideAlive`, or `KnownCondition` for a flag the simulator doesn't follow.
`ConditionalBranchTests` proves every branch equal to the game's lambda; `IntentForecastAudit` checks it in game; `IntentForecast.NextOn` asks a
simulated state.

### C5. Hook bodies

Every monster hook is read; `MonsterHookTests` holds each monster's coverage ledger entry to what its hooks' programs do.

### C6. Powers' and relics' hooks

A power's or relic's hook bodies are read the same way (`HookReader`) into the simulator's hook programs (`ModelSpec`, `HookProgram`, pure data
in Sim/Programs), and one generic model runs them (`ReadModel`), compiled once per class into delegates (decision #31).

| Game code | Read as |
|---|---|
| `CreatureCmd.GainBlock(base.Owner, amount or BlockVar, props)` | `GainBlockStep`: `SimEngine.ProcBlock`, as the hand-written models' `Block` |
| `CreatureCmd.Damage(HittableEnemies / NextItem(HittableEnemies) / power.Owner, amount or DamageVar, props, base.Owner or null)` | `DamageStep`: `ProcDamageAll`, `ProcDamageRandom` (a branch per enemy), `ProcDamageOn` |
| `CardPileCmd.Draw(count, base.Owner.Player)`, `PlayerCmd.GainEnergy(amount, base.Owner)` | `DrawStep`, `GainEnergyStep`: card events, each counted by a draw or energy tab |
| `PowerCmd.Apply<T>(base.Owner / HittableEnemies / power.Owner, amount, base.Owner)` | `ApplyPowerStep` (its debuff from the power's own `Type`, or `AllowNegative` with less than 0) |
| `PowerCmd.Remove(this)`, `Decrement(this)`, `TickDownDuration(this)` | `RemovePowerStep`, `DecrementPowerStep` |
| a test of the hook's parameters (`cardPlay.Card.Owner == base.Owner.Player`, `card.Type == Attack`, `applier == base.Owner`, `props.IsPoweredAttack()`, `participants.Contains(base.Owner)`...) | a `HookFact`, translated once for every model |
| `base.Amount`, `base.DynamicVars.Block` (its key from DynamicVarSet's getter), constants, arithmetic | `HookFactValue(Amount)`, `HookVar` (its canonical value from the class's CanonicalVars), `HookConstant`, `HookMath` |
| a Modify hook's returns (`ReadReturnedWays`) | `HookWays`: the value of the first way whose test holds |
| a store to its own field or property (`AttacksPlayedThisTurn++`), a read of it | `StoreFieldStep`, `HookField`: per-branch state (`SimPlayer.Fields`, `SimEnemy.Fields`) seeded from the game (`SimReaders.Fields`); a store nothing reads is left out |
| each test at its branch | `HookTest`: asked once a hook run, where a step first needs it |
| a temporary Strength or Dexterity power applied | the Strength or Dexterity its base class's BeforeApplied applies, by its sign |

What isn't read is a named gap (a store to the model's own state, its internal data, a hook no read model runs yet, a query with no fact), and a
hook the simulator never runs says why (outside fights: `OutOfCombatHooks.txt`; a relic's turn-start body that waits on its owner's next turn).
`HookProgramTests` reads every power's and relic's hooks on both branches and pins the counts. Which classes are read is a rule
(`ReadModels.Specs`): every power and relic class with no hand-written model (`SimModels.IsHandWritten`: it keeps its model until a parity proof
converts it) whose hooks read whole and that answers a hook the simulator runs. The classes the rule reads on both reviewed builds are a generated
record (`ReadModels.ReadWholeAtReview`, `ReadModels.g.cs`, held to both decompiles by `HookProgramTests`); one of them whose hooks the running
game's code no longer reads (a game update) is an unread spec (`ModelSpec.Unread`): reported once (`ReadModels.Report`), and every simulation
it's on the board in, or lands in, is unknown with why (`SimState.Unmodelled`, decision #92). A read model's tab is its commands' only where a
Proc Tags settings row for it exists (`ReadModels.HasTabRow`). `ReadModelParityTests` holds each read model to the hand-written model it replaced
(kept as it was in the test project) on every hook it answers.
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

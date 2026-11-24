using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Procs;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

public static partial class SimAdapter
{
    /// <summary>
    /// The play of <paramref name="card"/> aimed at <paramref name="targetId"/> (docs/SIMULATOR.md "Card scripts"): the card
    /// play with what each play does as actions (HP cost, attack, block, powers), worked out by the simulator from the card's
    /// values, the models and the captured terms; plus, as summary fields, what isn't an action yet (orbs, procs, hand changes).
    /// <paramref name="owner"/>: whose card it is (a teammate's auto-play: their Howl from Beyond); <paramref name="me"/> is then
    /// that teammate, and what the play gives its player goes to them.
    /// </summary>
    /// <remarks>A member it couldn't read (Sts2State.ReadScope) makes the play unknown (UnreadAction), named by the member.</remarks>
    public static SimPlay Play(CardModel card, PlayFacts facts, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int owner = Who.You)
    {
        using var reads = Sts2State.TrackReads();
        var play = PlayFromGame(card, facts, me, combat, ids, targetId, owner);
        if (reads.Unread.Count > 0) play.Before.Insert(0, new UnreadAction(reads.Unread[0]));
        return play;
    }

    /// <summary>
    /// Plays follow the card's program (decision #182). Off only in the comparison of the program's order with the one it replaced
    /// (CardProgramShadowTests).
    /// </summary>
    internal static bool UseCardPrograms = true;

    /// <summary>What a Corrupted card deals its owner on every play (Corrupted._damageAmount, a constant in its code).</summary>
    private const int CorruptedSelfDamage = 2;

    private static SimPlay PlayFromGame(CardModel card, PlayFacts facts, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int owner)
    {
        var name = card.GetType().Name;
        // The plays the simulator doesn't decide itself: Echo Form's it decides as the play starts (CardPlayAction.CountByModels).
        var playTarget = ids.FirstOrDefault(entry => entry.Value == targetId).Key;
        var plays = Math.Max(1, facts.Plays - CardPlays.ModelledReplays(card, playTarget));
        var play = new SimPlay
        {
            Name = name,
            IsAttack = card.Type == CardType.Attack,
            Plays = plays,
            // A card played from elsewhere (Howl from Beyond from the exhaust pile) doesn't leave the hand.
            // What its plays draw, make and take into your hand joins it as they run (decision #189); the cards its hooks bring back after.
            HandDelta = (card.Pile?.Type is PileType.Exhaust or PileType.Draw ? 0 : -1) + facts.ReturnsToHand,
            FadingDelta = IsFading(card, combat) ? -1 : 0,
            HexFadingDelta = IsFading(card, combat) && EtherealOnlyFromHex(card) ? -1 : 0,
        };
        // Aimed at a teammate: what lands on them goes to them (never to you, even with no teammate to aim at).
        Creature? ally = null;
        var allyId = Who.None;
        if (card.TargetType == TargetType.AnyAlly && Who.TeammateIndex(targetId) is var aimedAt and >= 0)
        {
            var allies = Teammates(me, combat);
            if (aimedAt < allies.Count) { play.AllyTarget = aimedAt; ally = allies[aimedAt].Creature; allyId = Who.Teammate(aimedAt); }
        }
        play.HandCard = card.Pile?.Type == PileType.Hand ? HandId(card) : 0;
        // Its draws join the hand as the simulator runs them (decision #189): none for the card leaving to account for.
        play.Draws = 0;
        // Whom its block and channels are for, read from its code (decision #182): a teammate it's aimed at (Lift, Ignition).
        play.BlockToAlly = Procs.Programs.CardEffects.BlocksAnother(card.GetType());
        play.ChannelToAlly = Procs.Programs.CardEffects.ChannelsForTarget(card.GetType());

        // ---- the card play and what each play does
        play.TargetId = card.TargetType == TargetType.AnyAlly ? allyId : targetId;
        play.Card = new SimCardPlay(SimReaders.Card(card, facts, me), owner, play.TargetId, PlayIndex: 0, plays, facts.AutoPlayed);
        if (facts.LosesHp) play.Body.Add(new LoseHpAction(Sts2State.Var(card, "HpLoss")));
        // A shot (ArrowCmd.ShootArrowAtTarget): the arrow leaves before the attack and impales the target after it. None from an X
        // shot at X 0.
        var shot = Fool.TriesShot(card);
        if (shot) play.Body.Add(new ShootAction());
        if (facts.AttackHits > 0 && AttackOf(card, facts, me, combat, ids, targetId, name) is { } attack)
        {
            attack = (shot ? attack with { NeedsArrow = true } : attack) with { StretchFromDamage = name == nameof(BlossomShot) };
            // A hit count the code works out on a condition the simulator asks (Spite: two hits when you lost HP this turn, else one):
            // the attack with each way's hits, on that way's condition, asked as it runs (decisions #190, #191).
            if (attack.HitsFrom == SimCount.Given && HitWaysOf(card) is { } ways)
                play.Body.AddRange(ways.Select(way => (SimAction)new WhenAction(way.Condition, new SimAction[] { attack with { Hits = way.Hits } })));
            else play.Body.Add(attack);
        }
        play.Body.AddRange(LaterAttacks(card, me, combat, ids));
        if (shot) play.Body.Add(new ImpaleAction());
        // Recall, Uproot, Wrest: every arrow of yours out of the target.
        if (Procs.Programs.CardEffects.DislodgesArrows(card.GetType())) play.Body.Add(new DislodgeAllAction());
        if (facts.BlockGains > 0 && BlockOf(card, facts, me, combat, ids, targetId, name, ally, allyId) is { } block) play.Body.Add(block);
        // Block for every player (Rally): the same value to each living teammate too, worked out on them (their Dexterity, Frail).
        if (facts.BlockGains > 0 && Procs.Programs.CardEffects.BlocksEachPlayer(card.GetType()) && card.DynamicVars.TryGetValue("Block", out var allBlock))
        {
            var mates = Teammates(me, combat);
            for (var teammateIndex = 0; teammateIndex < mates.Count; teammateIndex++)
            {
                if (mates[teammateIndex].Creature is not { IsAlive: true } mate) continue;
                play.Body.Add(new GainBlockAction(facts.BlockGains, allBlock.BaseValue, SimProps.Move)
                    { Captured = SimReaders.Block(combat, card, mate, allBlock.BaseValue, ValueProp.Move), To = Who.Teammate(teammateIndex) });
            }
        }
        // Teammates' draws and created cards (Huddle Up, Blade Symphony, Constellation's draws for its target): their card
        // events, for their procs. Indirect, like your own draws and created cards.
        var drawsForTarget = Procs.Programs.CardEffects.DrawsForTarget(card.GetType());
        if (Procs.Programs.CardEffects.DrawsForEachTeammate(card.GetType()) || TeammatesShivCards.Contains(name) || (drawsForTarget && ally != null))
        {
            var mates = Teammates(me, combat);
            var cards = Sts2State.Var(card, "Cards");
            for (var teammateIndex = 0; teammateIndex < mates.Count; teammateIndex++)
            {
                if (mates[teammateIndex].Creature is not { IsAlive: true } mate) continue;
                if (drawsForTarget && mate != ally) continue;
                var who = Who.Teammate(teammateIndex);
                if (TeammatesShivCards.Contains(name))
                {
                    play.Body.Add(new CardEventAction(CardEvent.Generated, cards) { Player = who, Indirect = true });
                    continue;
                }
                // Their draws: as many as their hand has room for and their piles hold; a shuffle when the draw pile runs out.
                var hand = PileType.Hand.GetPile(mates[teammateIndex]).Cards.Count;
                var drawPile = PileType.Draw.GetPile(mates[teammateIndex]).Cards.Count;
                var discard = PileType.Discard.GetPile(mates[teammateIndex]).Cards.Count;
                var drawn = Math.Min(cards, Math.Min(Math.Max(0, CardPile.MaxCardsInHand - hand), drawPile + discard));
                if (drawn > drawPile) play.Body.Add(new CardEventAction(CardEvent.Shuffled, Count: 1) { Player = who, Indirect = true });
                if (drawn > 0) play.Body.Add(new CardEventAction(CardEvent.Drawn, drawn) { Player = who, Indirect = true });
            }
        }
        if (Procs.Programs.CardEffects.TakesTargetsBlock(card.GetType())) play.Body.Add(new StripBlockAction(RemoveArtifact: true));
        play.Body.AddRange(PowersOf(card, facts, name, allyId));
        if (Procs.Programs.CardEffects.TriggersPoison(card.GetType())) play.Body.Add(new TriggerPoisonAction());
        play.Body.AddRange(OrbsOf(card, me, combat, name, play.ChannelToAlly ? allyId : owner));
        // The card's own energy (decision #117: Production, Sunder's kill, Double Energy...), each play.
        CardEnergy.AddTo(play, card, me, combat, ids.FirstOrDefault(entry => entry.Value == targetId).Key, owner, allyId);
        // A star gain on a condition the simulator asks (decision #190: Knockout Blow's kill); a certain one is the facts' (StarGains).
        foreach (var gain in Procs.Programs.CardEffects.Find(PlayOrder.ProgramOf(card.GetType()), command => command.Command is { DeclaringType: "PlayerCmd", Name: "GainStars" }))
            if (PlayConditions.Reading(card, gain.Condition) is { Way: Procs.Programs.ConditionWay.Simulated } reading)
                play.Body.Add(PlayConditions.Guarded(new CardEventAction(CardEvent.StarsGained, Count: 1), reading));
        // Ki Chant shuffles your discard pile in when your draw pile is short of what it looks at (each play, as it runs), then takes.
        if (Procs.Programs.CardEffects.ShufflesWhenShort(card.GetType()) && PlayFacts.LookCount(card) is > 0 and var looks)
            play.Body.Add(new ShuffleDrawPileAction(Array.Empty<SimDrawGroup>()) { WhenShorterThan = looks });
        play.Body.AddRange(TakesToHand(facts, me));
        AddCardEvents(play, facts, me);
        // Reboot: your hand into your draw pile and a shuffle (CardPileCmd.Shuffle) before its draws, right after the hand leaves.
        if (Procs.Programs.CardEffects.ShufflesDiscardPileIn(card.GetType()) && play.Body.FindIndex(action => action is TakeFromHandAction { Way: HandTakeWay.Each, Filter: null, Leaves: true }) is var leaves and >= 0)
            play.Body.Insert(leaves + 1, new ShuffleDrawPileAction(PileType.Hand.GetPile(me).Cards.Where(other => other != card)
                .Select(other => DrawProfiles.GetValueOrDefault(other)).OfType<SimDrawCard>().GroupBy(profile => profile.Key)
                .Select(sameProfile => new SimDrawGroup(sameProfile.First(), sameProfile.Count())).ToList()));
        // Cards it auto-plays from the top of your draw pile, after what it does to your hand (The Devil exhausts it first).
        // How many, whether they exhaust and whether they're upgraded first, read from its program (AutoPlaysOf): Havoc one that
        // exhausts, Cascade its X (one more upgraded), The Devil the cards in your hand as it plays, upgraded when it is.
        if (AutoPlaysOf(card) is { } autoPlays)
            play.Body.Add(new AutoPlayFromDrawAction(autoPlays.Count) { ForceExhaust = autoPlays.ForceExhaust, Upgrade = autoPlays.Upgrade, CountFrom = AutoPlayCountsTaken(card) ? SimCount.CardsTaken : SimCount.Given });
        // A replay of a card whose numbers change with each play in a way its code doesn't show (decision #160: Thrash).
        if (plays > 1 && PlayGrowth.Of(card).Unmodelled is { } growthUnknown) play.Body.Add(new NotModelledAction(growthUnknown));
        if (DrawnCardReaderOf(card.GetType()) is { } reader) UseDrawnCard(play, reader);

        // ---- summary fields (not actions yet)
        play.ByOsty = facts.ByOsty;
        // The rest of what each play does, after the card's own actions above: Capture Spirit's damage, Judgement, Bouncing Flask's
        // poison on random enemies, Osty's death (Sacrifice, Bone Shards; Necro Mastery reacts).
        var target2 = ids.FirstOrDefault(entry => entry.Value == targetId).Key;
        if (card.Type != CardType.Attack && Procs.Programs.CardEffects.DamagesTargetWithoutAttacking(card.GetType()) && target2 != null && card.DynamicVars.TryGetValue("Damage", out var tdVar) && tdVar is DamageVar tdv)
            play.Body.Add(new DamageAction(new DamageSpec(tdv.BaseValue, SimReaders.Props(tdv.Props))
                { Captured = new Dictionary<int, CapturedTerms> { [targetId] = SimReaders.Damage(me.RunState, combat, card, me.Creature, target2, tdv.BaseValue, tdv.Props) } }));
        if (Procs.Programs.CardEffects.SetsTargetsHp(card.GetType())) play.Body.Add(new ExecuteAction(Sts2State.Var(card, "Threshold")));
        if (card.TargetType == TargetType.RandomEnemy && card.DynamicVars.Values.FirstOrDefault(IsPowerVar<PoisonPower>) is { } flask)
            play.Body.Add(new ApplyPowerAction(PowerId.Poison, (int)flask.BaseValue)
                { To = ApplyPowerAction.Where.RandomEnemy, Times = Math.Max(1, Sts2State.Var(card, "Repeat", fallback: 1)), Debuff = true });
        if (Procs.Programs.CardEffects.KillsOsty(card.GetType())) play.Body.Add(new KillOstyAction());
        // What the card does, in the order its code does it (decision #182: its program).
        // Each action the facts built on its command's condition, when the simulator can ask it (decision #190: PlayConditions).
        if (UseCardPrograms) play.Body = PlayConditions.Guard(PlayOrder.ArrangeWithSteps(play.Body, PlayOrder.ProgramOf(card.GetType())), card);
        // The card's enchantment acts after the card itself, on every play (CardModel: EnchantmentModel.OnPlay after OnPlay). The
        // coverage ledger reviews every enchantment's OnPlay.
        switch (card.Enchantment)
        {
            // Adroit: its block (EnchantmentModel.OnPlay → CreatureCmd.GainBlock).
            case MegaCrit.Sts2.Core.Models.Enchantments.Adroit adroit:
                var baseValue = adroit.DynamicVars.Block.BaseValue;
                play.Body.Add(new GainBlockAction(Times: 1, baseValue, SimProps.Move) { Captured = SimReaders.Block(combat, card, me.Creature, baseValue, ValueProp.Move) });
                break;
            // Corrupted: damage to its owner, unblockable (Corrupted._damageAmount): your HP-loss modifiers and the damage hooks hear it
            // (Centennial Puzzle).
            case MegaCrit.Sts2.Core.Models.Enchantments.Corrupted when owner == Who.You:
                play.Body.Add(new LoseHpAction(CorruptedSelfDamage));
                break;
            case MegaCrit.Sts2.Core.Models.Enchantments.Corrupted:
                play.Body.Add(new NotModelledAction("a teammate's Corrupted card hurts them"));
                break;
            // Inky: Weak on the card's target, or on every enemy for a card that hits them all.
            case MegaCrit.Sts2.Core.Models.Enchantments.Inky inky when inky.DynamicVars.Values.FirstOrDefault(IsPowerVar<WeakPower>) is { } weak:
                play.Body.Add(new ApplyPowerAction(PowerId.Weak, (int)weak.BaseValue)
                    { To = card.TargetType == TargetType.AllEnemies ? ApplyPowerAction.Where.AllEnemies : ApplyPowerAction.Where.Target, Debuff = true });
                break;
        }
        // A teammate's card: the block it gives its player is theirs.
        if (owner != Who.You) play.Body = play.Body.Select(action => ToOwner(action, owner)).ToList();
        return play;
    }

    /// <summary>A teammate's card's block to you is theirs (an action on a condition too).</summary>
    private static SimAction ToOwner(SimAction action, int owner) => action switch
    {
        GainBlockAction { To: Who.You } gainBlockAction => gainBlockAction with { To = owner },
        WhenAction whenAction => whenAction with { Then = whenAction.Then.Select(guarded => ToOwner(guarded, owner)).ToList() },
        _ => action,
    };

    /// <summary>
    /// A card that acts on what its own draw gave (decision #118: DrawnCardReaderOf, read from its program): its draw and its reaction
    /// become one DrawnCardAction in the draw's place, so the reaction sees the card each branch drew. A block only for a type moves into
    /// it (Escape Plan: the game gains it after the draw, only for a Skill); a draw again while a type (Pillage); discards of the drawn
    /// cards that cost energy are the ones it makes there, not the plain count (Scrape).
    /// </summary>
    private static void UseDrawnCard(SimPlay play, DrawnCardReader reader)
    {
        var at = play.Body.FindIndex(simAction => simAction is CardEventAction { Event: CardEvent.Drawn });
        if (at < 0) return; // nothing drawn (a full hand, empty piles): nothing to act on
        var draw = (CardEventAction)play.Body[at];
        var action = new DrawnCardAction(draw.Count) { Indirect = draw.Indirect };
        if (reader.BlockIf)
        {
            if (play.Body.OfType<GainBlockAction>().FirstOrDefault() is { } block)
            {
                play.Body.Remove(block);
                action = action with { BlockIf = reader.Type, Block = block };
            }
        }
        else if (reader.DrawsAgain) action = action with { DrawAgainIf = reader.Type };
        else if (reader.What == DrawnCardReader.Reads.Cost)
        {
            play.Body.RemoveAll(simAction => simAction is CardEventAction { Event: CardEvent.Discarded });
            action = action with { DiscardUnlessFree = true };
        }
        play.Body[play.Body.FindIndex(simAction => simAction is CardEventAction { Event: CardEvent.Drawn })] = action;
    }

    /// <summary>
    /// The card events one play causes (decision #189: the play's own description, the engine repeats it), for the models that react to
    /// them: the energy it spends and gives back (before the play), what it takes from your hand (TakesFromHand: each play takes what it
    /// finds), its draws, the cards it makes and its star gains, the stars it spends (first play), its own exhaust and a shuffle (after
    /// the last play: it goes to its result pile once).
    /// </summary>
    private static void AddCardEvents(SimPlay play, PlayFacts facts, Player me)
    {
        static void Add(List<SimAction> to, CardEvent cardEvent, int count, bool indirect)
        {
            if (count > 0) to.Add(new CardEventAction(cardEvent, count) { Indirect = indirect });
        }
        Add(play.Before, CardEvent.EnergyGained, facts.EnergyBack, indirect: false);
        Add(play.Before, CardEvent.EnergySpent, facts.EnergySpent, indirect: false);
        // What it takes from your hand (discards, exhausts, put back) and its draws: PlayOrder puts them in its code's order (decision
        // #182), which decides the room the draws have; each play reads its hand as it runs.
        // Which cards it exhausts (what those that act when exhausted do: Drum of Battle), read from the hand as it is.
        if (facts.ExhaustsOthers > 0 && PlayFacts.ExhaustPickOf(facts.Card) is { FromDrawPile: false } exhaustPick) play.Body.Add(exhaustPick);
        var takes = TakesFromHand(facts.Card, play);
        play.Body.AddRange(takes);
        // The cards it discards that aren't in your hand: the ones it looked at on top of your draw pile (Ki Chant), on the board.
        Add(play.Body, CardEvent.Discarded, PlayFacts.DiscardedOutsideHand(facts.Card), indirect: true);
        // A Sly card it discards without a choice (the whole hand: CardCmd.Discard's AutoPlay, SlyDiscard) plays itself: not simulated
        // yet (tracker). One you pick is a card, never a particular one (decision #92): your own decision, neither simulated nor marked
        // (PlayFacts.MayDiscardSly leaves picks out).
        if (facts.MayDiscardSly) play.Body.Add(new NotModelledAction("a Sly card in your hand it discards plays itself"));
        // How many it takes into your hand is your choice (Neow's Fury): your hand after it isn't known.
        if (facts.HandTakeChosen) play.Body.Add(new NotModelledAction("you choose how many cards it takes into your hand"));
        play.Body.AddRange(DrawsOf(facts, me));
        AddMadeCards(play, facts, takes.Count > 0);
        Add(play.Body, CardEvent.StarsGained, facts.StarGains, indirect: false);
        // Spent with the energy, before the card does anything (PlayCardAction: SpendResources, then OnPlay): the play's first action,
        // so Mini Regent's Strength is on the card's own hits. Inside the play, as Galactic Dust and Child of the Stars read its stars.
        if (facts.Stars > 0) play.Body.Insert(0, new CardEventAction(CardEvent.StarsSpent, Count: 1) { FirstPlayOnly = true });
        if (facts.ExhaustsSelf) play.After.Add(new CardEventAction(CardEvent.Exhausted, Count: 1) { Card = play.Card!.Card });
        // Its own exhaust (Corruption: Skills exhaust) sets off its own AfterCardExhausted (Drum of Battle's energy).
        if (facts.ExhaustsSelf && CardEnergy.WhenExhausted(facts.Card) is > 0 and var ownExhaustEnergy)
            play.After.Add(new CardEventAction(CardEvent.EnergyGained, ownExhaustEnergy) { Player = play.Card!.Owner });
        if (facts.Shuffles) play.After.Add(new CardEventAction(CardEvent.Shuffled, Count: 1) { Indirect = true });
    }

    /// <summary>
    /// What one play takes from your hand, read from its program (decision #189: CardEffects' takes), each as a TakeFromHandAction the
    /// simulator decides as it runs: the exhausts of other cards (a pick, a roll, each card of a collection; a pick from your draw pile:
    /// Cleanse), the discards from your hand (a pick shared with an exhaust by a test on the card: The Hermit), the transforms in your hand
    /// (created cards; not when you choose how many), the cards put back on your draw pile. A take whose own test of the play fails on the
    /// board (True Grit's pick when it isn't upgraded) isn't one: nothing a play does changes it. A filtered take (Second Wind's non-Attacks)
    /// carries its cards in your hand now; one over every pile (Flak Cannon's statuses) takes its hand's share out silently, then counts every
    /// pile's. A loop's other commands run after each card (Second Wind's block: the play's block action moves into it).
    /// </summary>
    private static List<SimAction> TakesFromHand(CardModel card, SimPlay play)
    {
        var type = card.GetType();
        var program = Procs.Programs.CardPrograms.Of(type);
        var board = new Procs.Programs.ProgramBoard(card);
        var actions = new List<SimAction>();
        var serial = 0;
        int Number(Procs.Programs.ProgramValue? value) => value != null && Procs.Programs.ProgramEvaluator.Number(value, board, program) is { } read ? Math.Max(0, (int)read) : 1;
        var exhausts = Procs.Programs.CardEffects.ExhaustsOfOthers(type);
        var discards = Procs.Programs.CardEffects.Discards(type);
        void Take(Procs.Programs.CardEffects.CardsTaken take, CardEvent? cardEvent, CardEvent? eventIfNot = null, bool leaves = true)
        {
            if (Procs.Programs.ProgramEvaluator.Holds(take.PlayTest, board, program) == false) return;
            var key = $"{type.Name}#{serial++}";
            switch (take.Way)
            {
                case Procs.Programs.CardEffects.TakeWay.Picked when take.PickPile is MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw:
                    if (cardEvent == CardEvent.Exhausted)
                        actions.Add(new TakeFromHandAction(HandTakeWay.Pick) { Event = cardEvent, FromDrawPile = true, Most = Number(take.PerPick), Matching = PlayFacts.ExhaustOthers(card, new List<CardModel>(), DrawPileOfCard(card)) });
                    return;
                case Procs.Programs.CardEffects.TakeWay.Picked when take.PickPile is null or MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand:
                {
                    var filtered = !take.ItemTest.IsAlways || eventIfNot != null;
                    actions.Add(new TakeFromHandAction(HandTakeWay.Pick)
                    {
                        Event = cardEvent, EventIfNot = eventIfNot, Leaves = leaves, Most = Number(take.PerPick),
                        Filter = filtered ? key : null, Matching = filtered ? PlayFacts.MatchingAtStart(card, take, everyPile: false) : 0,
                    });
                    return;
                }
                case Procs.Programs.CardEffects.TakeWay.Random or Procs.Programs.CardEffects.TakeWay.Each:
                {
                    var way = take.Way == Procs.Programs.CardEffects.TakeWay.Random ? HandTakeWay.Random : take.Single ? HandTakeWay.Pick : HandTakeWay.Each;
                    var most = take.Way == Procs.Programs.CardEffects.TakeWay.Random ? Number(take.RollCount) : 1;
                    if (Procs.Programs.CardEffects.IsYourWholeHand(take.Items) && take.ItemTest.IsAlways)
                    {
                        actions.Add(new TakeFromHandAction(way) { Event = cardEvent, Leaves = leaves, Most = most, EachCard = EachCardOf(take, play, program) });
                        return;
                    }
                    if (PlayFacts.TakesFromEveryPile(card, take))
                    {
                        // Every pile's (Flak Cannon): its hand's share leaves first, then each card of every pile sets off its event.
                        actions.Add(new TakeFromHandAction(way) { Leaves = leaves, Most = most, Filter = key, Matching = PlayFacts.MatchingAtStart(card, take, everyPile: false) });
                        actions.Add(new TakeFromHandAction(way) { Event = cardEvent, Leaves = false, Most = most, Filter = "all:" + key, Matching = PlayFacts.MatchingAtStart(card, take, everyPile: true), EachCard = EachCardOf(take, play, program) });
                        return;
                    }
                    actions.Add(new TakeFromHandAction(way)
                    {
                        Event = cardEvent, Leaves = leaves, Most = most, Filter = key, Matching = PlayFacts.MatchingAtStart(card, take, everyPile: false), EachCard = EachCardOf(take, play, program),
                    });
                    return;
                }
            }
        }
        foreach (var exhaust in exhausts)
        {
            var shared = discards.FirstOrDefault(discard => discard.Pick != null && ReferenceEquals(discard.Pick, exhaust.Pick));
            Take(exhaust, CardEvent.Exhausted, shared != null ? CardEvent.Discarded : null);
        }
        foreach (var discard in discards)
            if (!exhausts.Any(exhaust => exhaust.Pick != null && ReferenceEquals(exhaust.Pick, discard.Pick)))
                Take(discard, CardEvent.Discarded);
        // Transformed in your hand: a created card each (CardCmd.Transform), the card staying in place; none certain when you choose how many.
        foreach (var transform in Procs.Programs.CardEffects.Transforms(type))
            if (!transform.ChoosesHowMany && transform.PickPile is null or MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand)
                Take(transform, CardEvent.Generated, leaves: false);
        // Put back on your draw pile from your hand, after its draws (Glimmer, Photon Cut, Thinking Ahead): you choose which.
        if (Procs.Programs.CardEffects.PutsBackFromHand(type)) actions.Add(new TakeFromHandAction(HandTakeWay.Pick) { Most = PlayFacts.PutBackCount(card) });
        // Your whole hand moved somewhere else (Reboot: into your draw pile): it leaves, setting off nothing.
        if (Procs.Programs.CardEffects.TakesWholeHand(type) && !actions.Any(action => action is TakeFromHandAction { Way: HandTakeWay.Each, Filter: null, Leaves: true }))
            actions.Add(new TakeFromHandAction(HandTakeWay.Each));
        return actions;
    }

    /// <summary>
    /// The filtered takes from your hand of a card's play (TakesFromHand's keys, in its order), each with the test a card in your hand
    /// passes to be taken: what a card joining your hand during the play counts for (SimDrawCard.TakenBy, CardEventAction.TakenBy).
    /// </summary>
    internal static List<(string Key, Func<CardModel, bool, bool?> Takes)> TakeFiltersOf(CardModel card)
    {
        var type = card.GetType();
        var program = Procs.Programs.CardPrograms.Of(type);
        var board = new Procs.Programs.ProgramBoard(card);
        var filters = new List<(string, Func<CardModel, bool, bool?>)>();
        var serial = 0;
        var exhausts = Procs.Programs.CardEffects.ExhaustsOfOthers(type);
        var discards = Procs.Programs.CardEffects.Discards(type);
        void Take(Procs.Programs.CardEffects.CardsTaken take, bool shared)
        {
            if (Procs.Programs.ProgramEvaluator.Holds(take.PlayTest, board, program) == false) return;
            var key = $"{type.Name}#{serial++}";
            switch (take.Way)
            {
                case Procs.Programs.CardEffects.TakeWay.Picked when take.PickPile is null or MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand:
                    if (!take.ItemTest.IsAlways || shared) filters.Add((key, (other, made) => Procs.Programs.ProgramEvaluator.HoldsFor(Tests(take.ItemTest, made), other, board, program)));
                    break;
                case Procs.Programs.CardEffects.TakeWay.Random or Procs.Programs.CardEffects.TakeWay.Each:
                    if (Procs.Programs.CardEffects.IsYourWholeHand(take.Items) && take.ItemTest.IsAlways) break;
                    filters.Add((key, (other, made) => (take.Filter is { } filter ? Procs.Programs.ProgramEvaluator.Passes(new Procs.Programs.LambdaValue(filter.Parameter, Tests(filter.Body, made)), other, board, program) : true) is var passes
                                              && Procs.Programs.ProgramEvaluator.HoldsFor(Tests(take.ItemTest, made), other, board, program) is var holds
                                              && passes != false && holds != false ? (passes == null || holds == null ? null : true) : false));
                    break;
            }
        }
        foreach (var exhaust in exhausts) Take(exhaust, shared: discards.Any(discard => discard.Pick != null && ReferenceEquals(discard.Pick, exhaust.Pick)));
        foreach (var discard in discards)
            if (!exhausts.Any(exhaust => exhaust.Pick != null && ReferenceEquals(exhaust.Pick, discard.Pick))) Take(discard, shared: false);
        foreach (var transform in Procs.Programs.CardEffects.Transforms(type))
            if (!transform.ChoosesHowMany && transform.PickPile is null or MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand) Take(transform, shared: false);
        // And the filters its conditions ask of your hand (decision #190: Impatience's Attacks), followed the same way.
        foreach (var filter in Procs.Programs.ConditionReader.HandFiltersOf(type, program))
            filters.Add((filter.Key, (other, _) => Procs.Programs.ProgramEvaluator.Passes(filter.Lambda, other, board, program)));
        return filters;
    }

    /// <summary>
    /// The filtered takes a card the play makes would be taken by (each of the card's CreateSteps, read on its canonical card), every
    /// pile's take as well as its hand's share; null when what it makes can't be told (a helper's cards) or a test can't be read on it.
    /// </summary>
    private static IReadOnlyList<string>? MadeTakenBy(CardModel card)
    {
        var filters = TakeFiltersOf(card);
        if (filters.Count == 0) return Array.Empty<string>();
        var made = Procs.Programs.CardPrograms.Of(card.GetType()).All().OfType<Procs.Programs.CreateStep>().Select(create => create.Card).Distinct().ToList();
        if (made.Count != 1 || ModelDb.AllCards.FirstOrDefault(canonical => canonical.GetType().Name == made[0]) is not { } madeCard) return null;
        var keys = new List<string>();
        foreach (var (key, takes) in filters)
        {
            // A made card is in a combat pile (never the exhaust pile): a test of its pile isn't asked of it.
            switch (takes(madeCard, true))
            {
                case true: keys.Add(key); keys.Add("all:" + key); break;
                case null: return null;
            }
        }
        return keys;
    }

    /// <summary>A take's test as asked of a card: of a card the play makes, without its tests of the card's pile (it's in a combat pile, never the exhaust pile).</summary>
    private static Procs.Programs.ProgramCondition Tests(Procs.Programs.ProgramCondition condition, bool made)
    {
        if (!made || condition.Terms.Count != 1) return condition;
        var kept = Procs.Programs.ProgramCondition.Always;
        foreach (var literal in condition.Terms[0])
            if (!literal.Test.Parts().Any(part => part is Procs.Programs.QueryValue { Name: "Pile" })) kept = kept.And(literal);
        return kept;
    }

    /// <summary>Cards in the owner's draw pile; 0 with none read.</summary>
    private static int DrawPileOfCard(CardModel card)
    {
        try { return card.Owner is { } owner ? MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw.GetPile(owner).Cards.Count : 0; } catch { return 0; }
    }

    /// <summary>
    /// What a take's loop does after each card besides taking it (Second Wind: block for each card exhausted): the play's block action, as
    /// one gain, moved out of the play into the take.
    /// </summary>
    private static IReadOnlyList<SimAction> EachCardOf(Procs.Programs.CardEffects.CardsTaken take, SimPlay play, Procs.Programs.CardProgram program)
    {
        var loop = program.All().OfType<Procs.Programs.LoopStep>()
            .FirstOrDefault(candidate => new Procs.Programs.CardProgram("", candidate.Body).Commands.Any(command => ReferenceEquals(command, take.Command)));
        if (loop == null || !new Procs.Programs.CardProgram("", loop.Body).Commands.Any(command => command.Command is { DeclaringType: "CreatureCmd", Name: "GainBlock" })) return Array.Empty<SimAction>();
        if (play.Body.OfType<GainBlockAction>().FirstOrDefault() is not { } block) return Array.Empty<SimAction>();
        play.Body.Remove(block);
        return new SimAction[] { block with { Times = 1 } };
    }

    /// <summary>
    /// One play's draws, each from its program (CardEffects.DrawsForYou), in order: a count of the cards the play took (Calculated Gamble)
    /// or of your hand's room (Scrawl) read as it runs; else its own count, on its condition (decision #190: PlayConditions). Then Swift's
    /// draw, the first play's only (Swift turns itself off). With no draw pile read, the cards join the hand as they're drawn.
    /// </summary>
    private static IEnumerable<SimAction> DrawsOf(PlayFacts facts, Player me)
    {
        var card = facts.Card;
        var type = card.GetType();
        var takes = Procs.Programs.CardEffects.ExhaustsOfOthers(type).Concat(Procs.Programs.CardEffects.Discards(type)).ToList();
        foreach (var draw in Procs.Programs.CardEffects.DrawsForYou(type))
        {
            if (takes.Any(take => Procs.Programs.CardEffects.CountsTaken(draw.Count, take)))
            {
                yield return new CardEventAction(CardEvent.Drawn, Count: 0) { Indirect = true, JoinsHand = true, CountFrom = SimCount.CardsTaken };
                continue;
            }
            if (Procs.Programs.CardEffects.ReadsHandRoom(draw.Count))
            {
                yield return new CardEventAction(CardEvent.Drawn, int.MaxValue) { Indirect = true, JoinsHand = true, CountFrom = SimCount.HandRoom };
                continue;
            }
            // On its condition (decision #190): asked by the simulator as each play runs (FTL's plays this turn, Fetch's first play, Ki
            // Hop's and Wrest's arrows, Impatience's hand), or decided on the board when nothing in the play can change it.
            var reading = PlayConditions.Reading(card, draw.Condition);
            var drawn = PlayFacts.DrawCount(card, draw);
            if (drawn <= 0 || PlayConditions.Skipped(card, draw.Condition, reading)) continue;
            foreach (var ask in PlayConditions.AsksOf(reading)) yield return ask;
            yield return PlayConditions.Guarded(new CardEventAction(CardEvent.Drawn, drawn) { Indirect = true, JoinsHand = true }, reading);
        }
        if (facts.EnchantmentDraws > 0)
            yield return new FirstPlayOnly(new SimAction[] { new CardEventAction(CardEvent.Drawn, facts.EnchantmentDraws) { Indirect = true, JoinsHand = true } });
    }

    /// <summary>
    /// The cards one play makes (PlayFacts: Generated, its statuses and Sadness; transforms outside your hand), into your hand as they're
    /// made when they go there (CardCode.MadeInto), each play's count its own or read as it runs (the cards the play took: Storm of Steel;
    /// your hand's room: The Moon). The Sovereign Blade a forge makes is the first play's only: a later play finds it.
    /// </summary>
    private static void AddMadeCards(SimPlay play, PlayFacts facts, bool takes)
    {
        var forged = facts.ForgesBlade ? 1 : 0;
        var sadness = facts.SadnessCreated;
        var statuses = Math.Max(0, facts.GeneratedStatuses - sadness);
        var plain = Math.Max(0, facts.Generated - forged - statuses - sadness);
        var made = plain + statuses + sadness;
        var inHand = Math.Max(0, facts.GeneratedInHand - forged);
        var from = takes || facts.GeneratedFrom != SimCount.CardsTaken ? facts.GeneratedFrom : SimCount.Given;
        // What its filtered takes would make of each card it makes, for a later play (decision #189).
        var takenBy = made > 0 ? MadeTakenBy(facts.Card) : null;
        void Made(CardEvent cardEvent, int count, bool joinsHand)
        {
            if (from != SimCount.Given && made > 0)
            {
                if (count > 0) play.Body.Add(new CardEventAction(cardEvent, from == SimCount.HandRoom ? int.MaxValue : 0) { Indirect = true, JoinsHand = joinsHand, CountFrom = from, TakenBy = takenBy });
                return;
            }
            if (count > 0) play.Body.Add(new CardEventAction(cardEvent, count) { Indirect = true, JoinsHand = joinsHand, TakenBy = takenBy });
        }
        var allInHand = inHand >= made;
        // Into your hand first, the rest elsewhere (Severance: one of three into your hand).
        if (!allInHand && inHand > 0 && from == SimCount.Given)
        {
            Made(CardEvent.Generated, Math.Min(inHand, plain), joinsHand: true);
            Made(CardEvent.Generated, plain - Math.Min(inHand, plain), joinsHand: false);
        }
        else Made(CardEvent.Generated, plain, allInHand);
        Made(CardEvent.GeneratedStatus, statuses, allInHand);
        Made(CardEvent.GeneratedSadness, sadness, allInHand);
        // Transformed outside your hand (Charge, Seance: your draw pile): a created card each, in place.
        if (PlayFacts.TransformsOutsideHand(facts.Card) is > 0 and var transformed && !PlayFacts.TransformsChosen(facts.Card))
            play.Body.Add(new CardEventAction(CardEvent.Generated, transformed) { Indirect = true });
        if (forged > 0) play.Body.Add(new FirstPlayOnly(new SimAction[] { new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true } }));
    }

    private static bool IsPowerVar<T>(DynamicVar dynamicVar) where T : PowerModel =>
        dynamicVar.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(PowerVar<>) && type.GetGenericArguments()[0] == typeof(T);

    /// <summary>
    /// The attack's hit count's ways when its code works it out on a test the simulator asks (WithHitCount's argument a value two ways
    /// leave: Spite's {not LostHpThisTurn: 1; LostHpThisTurn: HitCount}): each way's condition (after the ways before it fail, as the first
    /// way that holds gives the value) and its hits, the card's own number. Null when it isn't such a count.
    /// </summary>
    private static List<(SimCondition Condition, int Hits)>? HitWaysOf(CardModel card)
    {
        if (Procs.Programs.CardEffects.Attacks(card.GetType()) is not [var attackStep, ..]) return null;
        var value = attackStep.Command.HitCountValue;
        while (value is Procs.Programs.StoredValue stored) value = stored.Value;
        if (value is not Procs.Programs.WaysValue { Ways.Count: > 1 } ways) return null;
        var program = Procs.Programs.CardPrograms.Of(card.GetType());
        var board = new Procs.Programs.ProgramBoard(card);
        var result = new List<(SimCondition Condition, int Hits)>();
        var before = new List<SimCondition>();
        foreach (var (when, hits) in ways.Ways)
        {
            var reading = PlayConditions.Reading(card, when);
            if (reading is not { Way: Procs.Programs.ConditionWay.Simulated, Condition: { } condition, AskFirst.Count: 0 }) return null;
            if (Procs.Programs.ProgramEvaluator.Number(hits, board, program) is not { } count) return null;
            result.Add((before.Count == 0 ? condition : new AllCondition(before.Select(earlier => (SimCondition)new NotCondition(earlier)).Append(condition).ToList()), (int)count));
            before.Add(condition);
        }
        return result;
    }

    /// <summary>
    /// The card's attack, each play: its hits per play, its damage value (calculated on each target when it's a calculated
    /// value) and props, and the captured terms on each enemy. Osty's attacks keep the game's final numbers (his own powers
    /// aren't simulated). Timber's kill attack and Rain Shot's splash ride along.
    /// </summary>
    private static AttackAction? AttackOf(CardModel card, PlayFacts facts, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, string name)
    {
        var mode = card.TargetType switch
        {
            TargetType.AnyEnemy => SimPlay.Target.Single,
            TargetType.AllEnemies => SimPlay.Target.All,
            TargetType.RandomEnemy => SimPlay.Target.Random,
            _ => SimPlay.Target.None,
        };
        if (mode == SimPlay.Target.None) return null;
        var aimed = ids.FirstOrDefault(entry => entry.Value == targetId).Key;
        var hits = mode == SimPlay.Target.Single && aimed != null ? facts.HitsOn(aimed) : facts.AttackHits;
        // A hit count that counts the cards a take of the play took (Fiend Fire: its hand), read as it runs (decision #189).
        var hitsTaken = Procs.Programs.CardEffects.Attacks(card.GetType()) is [var attackStep, ..]
                        && Procs.Programs.CardEffects.ExhaustsOfOthers(card.GetType()).Concat(Procs.Programs.CardEffects.Discards(card.GetType())).Any(take => Procs.Programs.CardEffects.CountsTaken(attackStep.Command.HitCountValue, take));
        if (hits <= 0 && !hitsTaken) return null;

        var baseOn = new Dictionary<int, decimal>();
        var captured = new Dictionary<int, CapturedTerms>();
        decimal baseDamage = 0m;
        var props = ValueProp.Move;
        CalculatedVar? calc = null;
        if (card.DynamicVars.TryGetValue("CalculatedDamage", out var dynamicVar) && dynamicVar is CalculatedVar calculatedVar)
        {
            calc = calculatedVar;
            if (dynamicVar is CalculatedDamageVar cdv) props = cdv.Props;
        }
        else if ((card.DynamicVars.TryGetValue("Damage", out var otherDynamicVar) || card.DynamicVars.TryGetValue("OstyDamage", out otherDynamicVar)) && otherDynamicVar is DamageVar damageVar)
        {
            baseDamage = damageVar.BaseValue;
            props = damageVar.Props;
        }
        var fixedNumbers = facts.ByOsty || (calc == null && baseDamage == 0m);
        foreach (var (creature, id) in ids)
        {
            if (!facts.CanHit(creature)) continue;
            if (fixedNumbers)
            {
                baseOn[id] = facts.DamagePerHit(creature);
                continue;
            }
            var baseValue = calc != null ? SafeDecimal(() => calc.Calculate(creature)) : baseDamage;
            baseOn[id] = baseValue;
            captured[id] = SimReaders.Damage(me.RunState, combat, card, me.Creature, creature, baseValue, props);
        }
        var growth = facts.Plays > 1 && !fixedNumbers ? PlayGrowth.Of(card) : PlayGrowth.Growth.None;
        return new AttackAction(hits, mode, new DamageSpec(baseDamage, SimReaders.Props(props))
            { BaseOn = baseOn, Captured = captured, Fixed = fixedNumbers, GrowsEachPlay = growth.Damage, EnchantFirstPlayOnly = growth.EnchantFirstPlayOnly })
        {
            HitsFrom = hitsTaken ? SimCount.CardsTaken : SimCount.Given,
            ByOsty = facts.ByOsty, // a single target: the play's (SimPlay.TargetId)
            Splash = PlayFacts.SplashCards.Contains(name), // the target's damage to every other enemy
            RepeatPerKill = RepeatPerKillCards.Contains(name),
        };
    }

    /// <summary>
    /// The play's later attacks at every enemy, each on its condition (decision #190: Timber's second attack when the first killed): its
    /// damage the card value its code names, through the pipeline on each enemy, a whole attack with its hooks (DamageCmd.Attack...
    /// TargetingAllOpponents...Execute). Only one whose condition the simulator asks: the play's first attack is AttackOf's.
    /// </summary>
    private static IEnumerable<SimAction> LaterAttacks(CardModel card, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids)
    {
        foreach (var later in Procs.Programs.CardEffects.Attacks(card.GetType()).Skip(1))
        {
            if (!later.Command.Builder.Contains("TargetingAllOpponents") || later.Command.Values.Count == 0
                || later.Command.Values[0].Parts().OfType<Procs.Programs.CardValue>().FirstOrDefault() is not { } value
                || !card.DynamicVars.TryGetValue(value.Key, out var dynamicVar) || dynamicVar is not DamageVar damageVar
                || PlayConditions.Reading(card, later.Condition) is not { Way: Procs.Programs.ConditionWay.Simulated } reading) continue;
            var captured = new Dictionary<int, CapturedTerms>();
            foreach (var (creature, id) in ids)
                if (creature.IsAlive && creature.IsHittable) captured[id] = SimReaders.Damage(me.RunState, combat, card, me.Creature, creature, damageVar.BaseValue, damageVar.Props);
            var attack = new AttackAction(Hits: 1, SimPlay.Target.All, new DamageSpec(damageVar.BaseValue, SimReaders.Props(damageVar.Props)) { Captured = captured });
            yield return PlayConditions.Guarded(attack, reading);
        }
    }

    /// <summary>
    /// The card's own block, each play: its block value through the block pipeline (models and captured terms), to you or the
    /// targeted teammate. Fisticuffs' block is the damage it dealt, a powered Move through the pipeline (Dexterity, Frail).
    /// Other cards whose block isn't a value (Entrench: your block; Mimic and a calculated block aimed at a teammate) keep the
    /// game's number.
    /// </summary>
    private static GainBlockAction? BlockOf(CardModel card, PlayFacts facts, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, string name, Creature? ally, int allyId)
    {
        var gains = facts.BlockGains;
        if (gains <= 0) return null;
        var toAlly = Procs.Programs.CardEffects.BlocksAnother(card.GetType());
        var to = toAlly ? allyId : Who.You;
        if (toAlly && allyId == Who.None) return null; // no teammate to aim at: the block goes to no one
        var aimed = ids.FirstOrDefault(entry => entry.Value == targetId).Key;
        if (name == nameof(Fisticuffs))
        {
            // Fisticuffs.OnPlay: CreatureCmd.GainBlock(the damage dealt, ValueProp.Move), so Dexterity and Frail apply.
            var dealt = aimed != null ? facts.TotalDamage(aimed) : 0;
            return new GainBlockAction(gains, dealt, SimProps.Move) { Captured = SimReaders.Block(combat, card, me.Creature, dealt, ValueProp.Move), To = to };
        }
        int? fixedEach = name switch
        {
            nameof(Entrench) => me.Creature.Block,
            _ when toAlly && card.DynamicVars.ContainsKey("CalculatedBlock") => ally != null ? CardBlock(card, combat, me, ally, ally) : 0,
            nameof(Mimic) when ally != null => CardBlock(card, combat, me, me.Creature, ally),
            _ => null,
        };
        if (fixedEach is { } each) return new GainBlockAction(gains, each, SimProps.Move) { Fixed = true, To = to };
        decimal baseValue;
        if (card.DynamicVars.TryGetValue("CalculatedBlock", out var dynamicVar) && dynamicVar is CalculatedVar calc) baseValue = SafeDecimal(() => calc.Calculate(null));
        else if (card.DynamicVars.TryGetValue("Block", out var otherDynamicVar)) baseValue = otherDynamicVar.BaseValue;
        else return new GainBlockAction(gains, CardBlock(card, combat, me), SimProps.Move) { Fixed = true, To = to };
        var gainer = toAlly && ally != null ? ally : me.Creature;
        var growth = facts.Plays > 1 ? PlayGrowth.Of(card) : PlayGrowth.Growth.None;
        return new GainBlockAction(gains, baseValue, SimProps.Move)
        {
            Captured = SimReaders.Block(combat, card, gainer, baseValue, ValueProp.Move), To = to, GrowsEachPlay = growth.Block,
            EnchantFirstPlayOnly = growth.EnchantFirstPlayOnly,
        };
    }

    /// <summary>
    /// The powers the card's code applies (CardCoverage.PowersApplied), each play: buffs to you (or the targeted teammate:
    /// Intercept's Covered), the debuffs PlayFacts finds it lands on enemies to its target or every enemy, and the Strength it
    /// takes or gives (EnemyStrengthOf). Amounts from the card's PowerVar, its plain value, or 1. Only powers the simulator
    /// reads: modelled ones, the ones it keeps as numbers, and poison.
    /// </summary>
    private static IEnumerable<SimAction> PowersOf(CardModel card, PlayFacts facts, string name, int allyId)
    {
        var actions = new List<SimAction>();
        int AmountOf(Type type)
        {
            foreach (var dynamicVar in card.DynamicVars.Values)
            {
                var otherType = dynamicVar.GetType();
                if (otherType.IsGenericType && otherType.GetGenericTypeDefinition() == typeof(PowerVar<>) && otherType.GetGenericArguments()[0] == type) return (int)dynamicVar.BaseValue;
            }
            return PlainSelfPowerAmount(card, type) ?? 1;
        }
        bool Simulated(string powerName) => SimModels.Has(powerName) || SelfPowers.Contains(powerName) || powerName == nameof(PoisonPower) || powerName == nameof(ArtifactPower);
        var enemyStrength = EnemyStrengthOf(card);
        var debuffs = facts.EnemyDebuffs.Concat(facts.EnemyTemporaryDebuffs).ToHashSet();
        var whereEnemies = card.TargetType switch
        {
            TargetType.AnyEnemy => ApplyPowerAction.Where.Target,
            TargetType.AllEnemies => ApplyPowerAction.Where.AllEnemies,
            _ => (ApplyPowerAction.Where?)null,
        };
        foreach (var type in Diagnostics.CardCoverage.PowersApplied(card.GetType()))
        {
            var simPowerName = SimPowerName(type);
            if (!Simulated(simPowerName) && !(PlayFacts.IsDebuffType(type) && debuffs.Contains(type))) continue;
            if (simPowerName == nameof(PoisonPower) && card.TargetType == TargetType.RandomEnemy) continue; // Bouncing Flask: random, a summary field
            // A temporary Strength loss on enemies (Piercing Wail, Dark Shackles: TemporaryStrengthPower) is the Strength action below
            // (EnemyStrengthOf): one debuff, so one Artifact charge takes all of it (issue #371: applied twice, Artifact took the first
            // and the Strength still landed).
            if (typeof(TemporaryStrengthPower).IsAssignableFrom(type) && enemyStrength.Any(change => change.Power == type)) continue;
            if (PlayFacts.IsDebuffType(type))
            {
                if (!debuffs.Contains(type) || whereEnemies is not { } where) continue;
                actions.Add(new ApplyPowerAction(new PowerId(simPowerName), AmountOf(type)) { To = where, Debuff = true, CopiesTarget = AppliesTargetsAmount(card.GetType(), type.Name) });
            }
            else if (card.TargetType == TargetType.AnyAlly && allyId != Who.None && simPowerName == nameof(CoveredPower))
            {
                actions.Add(new ApplyPowerAction(new PowerId(simPowerName), AmountOf(type)) { To = ApplyPowerAction.Where.Creature, Creature = allyId });
            }
            else if (simPowerName == nameof(StretchPower) && Procs.Programs.CardEffects.KeepsStretchForReplays(card.GetType()))
            {
                // Cho Ki: your Stretch again, as much as you have (so the play keeps it).
                actions.Add(new ApplyPowerAction(new PowerId(simPowerName), Amount: 0) { To = ApplyPowerAction.Where.You, CopiesOwn = true });
            }
            else
            {
                actions.Add(new ApplyPowerAction(new PowerId(simPowerName), AmountOf(type)) { To = ApplyPowerAction.Where.You });
            }
        }
        // Strength the card takes from enemies (this turn or for good) or gives them (Fight Me).
        foreach (var (_, amount, all) in enemyStrength)
            actions.Add(new ApplyPowerAction(PowerId.Strength, amount) { To = all ? ApplyPowerAction.Where.AllEnemies : ApplyPowerAction.Where.Target, Debuff = amount < 0 });
        return actions;
    }

    /// <summary>
    /// Every PowerCmd.Apply of <paramref name="power"/> the card's code makes applies the target's own amount of it, as it is when it lands
    /// (Molten Fist: its Vulnerable again), read from the amount argument (ConditionReader.TargetPowerOf).
    /// </summary>
    private static bool AppliesTargetsAmount(Type cardType, string power)
    {
        var applies = Procs.Programs.CardPrograms.Of(cardType).Commands
            .Where(command => command.Command is { DeclaringType: "PowerCmd", Name: "Apply" } && command.Command.GenericArguments.SequenceEqual(new[] { power })).ToList();
        return applies.Count > 0 && applies.All(apply => apply.Values.Count > 2 && Procs.Programs.ConditionReader.TargetPowerOf(Unstored(apply.Values[2])) == power);
    }

    /// <summary>
    /// The Strength a play takes from enemies or gives them, read from its program (decision #182): each PowerCmd.Apply of StrengthPower
    /// or of a temporary Strength power (TemporaryStrengthPower: Piercing Wail, Dark Shackles take Strength until the end of the turn, a
    /// debuff's sign) to its target when it's aimed at an enemy, or to every enemy you can hit (a loop over them, or all of them at once:
    /// Crush Under), on its condition: the power, the amount (its argument on the board: Shared Fate's -EnemyStrengthLoss, Resonance's -1)
    /// and whether it's every enemy.
    /// </summary>
    internal static List<(Type Power, int Amount, bool All)> EnemyStrengthOf(CardModel card)
    {
        var changes = new List<(Type, int, bool)>();
        var program = Procs.Programs.CardPrograms.Of(card.GetType());
        var board = new Procs.Programs.ProgramBoard(card);
        var applied = Diagnostics.CardCoverage.PowersApplied(card.GetType());
        foreach (var apply in Procs.Programs.CardEffects.Find(program, command => command.Command is { DeclaringType: "PowerCmd", Name: "Apply", GenericArguments.Count: 1 }))
        {
            if (applied.FirstOrDefault(type => type.Name == apply.Command.Command.GenericArguments[0]) is not { } power
                || power != typeof(StrengthPower) && !typeof(TemporaryStrengthPower).IsAssignableFrom(power)
                || apply.Command.Values.Count < 3 || Procs.Programs.ProgramEvaluator.Holds(apply.Condition, board, program) == false) continue;
            var whom = apply.Command.Values[1];
            var all = whom.Parts().Concat(apply.Loops.SelectMany(loop => loop.Items?.Parts() ?? Array.Empty<Procs.Programs.ProgramValue>()))
                .Any(part => part is Procs.Programs.QueryValue { Name: "HittableEnemies" });
            var atTarget = card.TargetType == TargetType.AnyEnemy && whom.Parts().Any(part => part is Procs.Programs.QueryValue { Name: "Target", Of: Procs.Programs.NamedValue { Name: "cardPlay" } });
            if (!all && !atTarget) continue;
            var amount = Procs.Programs.ProgramEvaluator.Number(apply.Command.Values[2], board, program) is { } read ? (int)read : 1;
            var sign = typeof(TemporaryStrengthPower).IsAssignableFrom(power) && PlayFacts.IsDebuffType(power) ? -1 : 1;
            changes.Add((power, sign * amount, all));
        }
        return changes;
    }

    /// <summary>How a card's play takes cards into your hand (HandTakeOf).</summary>
    /// <param name="Piles">The piles it takes them from (your draw pile, your discard pile; both for a collection over every pile).</param>
    /// <param name="Eligible">Which cards there it can take.</param>
    /// <param name="Random">The pick is a roll (TakeRandom); else it's yours.</param>
    /// <param name="AllAtOnce">Every card it takes, on its first play (PlayFacts.TakenToHand); else one a play.</param>
    internal sealed record HandTake(IReadOnlyList<PileType> Piles, Func<CardModel, bool> Eligible, bool Random = false, bool AllAtOnce = false);

    /// <summary>
    /// How the card's play takes cards into your hand, read from its program (CardEffects.TakesIntoHandOf: its CardPileCmd.Add to the
    /// hand), so the cards it takes leave their pile in the simulation (TakeToHandAction): a pick from a pile, of the cards its filter
    /// lets through (Secret Weapon's Attacks), one a play unless it picks more (Dredge); a roll from a collection (Anointed's Rares, The
    /// World's discard pile), or each card of one (All For One's 0-cost cards, Summon Forth's Sovereign Blades from every pile), all on
    /// the first play. Null when it takes none, or you choose how many (Neow's Fury), or what it takes from can't be read.
    /// </summary>
    internal static HandTake? HandTakeOf(CardModel card)
    {
        var takes = Procs.Programs.CardEffects.TakesIntoHandOf(card.GetType());
        if (takes.Count == 0 || takes.Any(take => take.ChoosesHowMany)) return null;
        var take = takes[0];
        var program = Procs.Programs.CardPrograms.Of(card.GetType());
        var board = new Procs.Programs.ProgramBoard(card);
        switch (take.Way)
        {
            case Procs.Programs.CardEffects.TakeWay.Picked when take.PickPile is { } pile && pile != PileType.Hand:
                return new HandTake(new[] { pile }, other => (take.PickFilter is not { } filter || Procs.Programs.ProgramEvaluator.Passes(filter, other, board, program) != false)
                                                             && Procs.Programs.ProgramEvaluator.HoldsFor(take.ItemTest, other, board, program) != false,
                    AllAtOnce: take.PerPick is not Procs.Programs.ConstantValue { Value: 1 });
            case Procs.Programs.CardEffects.TakeWay.Random or Procs.Programs.CardEffects.TakeWay.Each when take.Items != null
                                                                                                       && Procs.Programs.ProgramEvaluator.Items(take.Items, board, program) is { } items:
            {
                var cards = items.OfType<CardModel>().Where(other => !ReferenceEquals(other, card)).ToHashSet(ReferenceEqualityComparer.Instance);
                // The piles its collection is over (a pile's cards, or every pile: All Cards).
                var piles = take.Items.Parts().OfType<Procs.Programs.QueryValue>().Where(value => value is { Name: "GetPile", Arguments: [Procs.Programs.ConstantValue, ..] })
                    .Select(value => (PileType)((Procs.Programs.ConstantValue)value.Arguments[0]).Value).Distinct().ToList();
                if (piles.Count == 0) piles = new List<PileType> { PileType.Draw, PileType.Discard };
                return new HandTake(piles, other => cards.Contains(other), Random: take.Way == Procs.Programs.CardEffects.TakeWay.Random, AllAtOnce: true);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// What one play takes into your hand from your piles (HandTakeOf), each play on its own (decision #189): each pile's cards it can
    /// take, by profile (DrawProfiles), as a TakeToHandAction whose cards join your hand as they come (into a full hand, the discard pile).
    /// How many a play takes: a pick's most, a roll's count (read as it runs when it counts your hand's room: The World, Anointed), every
    /// card of a collection (All For One; a later play finds them gone). None with no pile read.
    /// </summary>
    private static IEnumerable<SimAction> TakesToHand(PlayFacts facts, Player me)
    {
        if (facts.TakenToHand <= 0 || HandTakeOf(facts.Card) is not { } take) yield break;
        var (count, from) = HandTakeCount(facts.Card);
        foreach (var pile in take.Piles)
        {
            if (pile is not (PileType.Draw or PileType.Discard)) continue;
            var counts = Eligible(me, pile == PileType.Discard, take.Eligible);
            // A play that shuffles your discard pile in first when your draw pile is short (Ki Chant) can take from what it shuffles in.
            if (pile == PileType.Draw && Procs.Programs.CardEffects.ShufflesWhenShort(facts.Card.GetType()))
                foreach (var (key, inDiscard) in Eligible(me, fromDiscard: true, take.Eligible)) counts[key] = counts.GetValueOrDefault(key) + inDiscard;
            if (counts.Count == 0) continue;
            yield return new TakeToHandAction(take.Piles.Count > 1 ? int.MaxValue : count, counts)
                { Random = take.Random, FromDiscard = pile == PileType.Discard, JoinsHand = true, CountFrom = from };
        }
    }

    /// <summary>How many cards one play takes into your hand (its take's pick, roll or collection), and whether the simulator reads it as it runs.</summary>
    private static (int Count, SimCount From) HandTakeCount(CardModel card)
    {
        var take = Procs.Programs.CardEffects.TakesIntoHandOf(card.GetType())[0];
        var program = Procs.Programs.CardPrograms.Of(card.GetType());
        var board = new Procs.Programs.ProgramBoard(card);
        int Number(Procs.Programs.ProgramValue value) => Procs.Programs.ProgramEvaluator.Number(value, board, program) is { } read ? Math.Max(0, (int)read) : 1;
        switch (take.Way)
        {
            case Procs.Programs.CardEffects.TakeWay.Picked:
                return (Number(take.PerPick), SimCount.Given);
            case Procs.Programs.CardEffects.TakeWay.Random when take.RollCount is { } roll:
                if (!Procs.Programs.CardEffects.ReadsHandRoom(roll)) return (Number(roll), SimCount.Given);
                return (Procs.Programs.CardEffects.WithoutRoom(roll) is { } capped ? Number(capped) : int.MaxValue, SimCount.HandRoom);
            case Procs.Programs.CardEffects.TakeWay.Random:
                return (1, SimCount.Given);
            default:
                return (int.MaxValue, SimCount.Given);
        }
    }

    /// <summary>
    /// The cards a play auto-plays from the top of your draw pile (CardPileCmd.AutoPlayFromDrawPile in its program): how many (its count
    /// argument on the board: Havoc 1, Cascade its X and one more upgraded, The Devil the cards in your hand), whether they exhaust (its
    /// exhaust argument: Havoc's), and whether they're upgraded first (the callback it gives upgrading them, on its condition: The Devil
    /// upgraded). Null for a card that auto-plays none.
    /// </summary>
    internal static (int Count, bool ForceExhaust, bool Upgrade)? AutoPlaysOf(CardModel card)
    {
        var program = Procs.Programs.CardPrograms.Of(card.GetType());
        if (Procs.Programs.CardEffects.Find(program, command => command.Command is { DeclaringType: "CardPileCmd", Name: "AutoPlayFromDrawPile" }) is not [var autoPlay, ..])
            return null;
        var board = new Procs.Programs.ProgramBoard(card);
        var values = autoPlay.Command.Values;
        var count = values.Count > 2 && Procs.Programs.ProgramEvaluator.Number(values[2], board, program) is { } read ? (int)read : 1;
        var exhausts = values.Count > 4 && values[4] is Procs.Programs.ConstantValue { Value: not 0 };
        var upgrades = autoPlay.Command.Inner.OfType<Procs.Programs.CommandStep>().Any(inner => inner.Command is { DeclaringType: "CardCmd", Name: "Upgrade" }
                                                                                              && Procs.Programs.ProgramEvaluator.Holds(inner.Condition ?? Procs.Programs.ProgramCondition.Always, board, program) != false);
        return (Math.Max(0, count), exhausts, upgrades);
    }

    /// <summary>The count of cards it auto-plays counts the cards a take of the play took (The Devil: one per card it exhausted), read as it runs.</summary>
    private static bool AutoPlayCountsTaken(CardModel card) =>
        Procs.Programs.CardEffects.Find(Procs.Programs.CardPrograms.Of(card.GetType()), command => command.Command is { DeclaringType: "CardPileCmd", Name: "AutoPlayFromDrawPile" }) is [var autoPlay, ..]
        && autoPlay.Command.Values.Count > 2
        && Procs.Programs.CardEffects.ExhaustsOfOthers(card.GetType()).Any(take => Procs.Programs.CardEffects.CountsTaken(autoPlay.Command.Values[2], take));

    /// <summary>How many cards of each profile in your draw or discard pile pass the filter.</summary>
    private static Dictionary<string, int> Eligible(Player me, bool fromDiscard, Func<CardModel, bool> eligible)
    {
        var counts = new Dictionary<string, int>();
        foreach (var other in (fromDiscard ? PileType.Discard : PileType.Draw).GetPile(me).Cards)
            if (eligible(other) && DrawProfiles.TryGetValue(other, out var profile)) counts[profile.Key] = counts.GetValueOrDefault(profile.Key) + 1;
        return counts;
    }

    /// <summary>
    /// What each play does to orbs, in order: slots it adds or removes, orbs it evokes, orbs it channels (into the targeted
    /// teammate's for Ignition; none when there's no teammate to aim at), then the Focus it gives or takes. Channelled orbs get
    /// their values when they're channelled. Storm and Trash to Treasure channel from their models.
    /// </summary>
    private static IEnumerable<SimAction> OrbsOf(CardModel card, Player me, ICombatState combat, string name, int channelTo)
    {
        var actions = new List<SimAction>();
        // What it does to your orbs, read from its program (PlayFacts.OrbsOf): slots, evokes, then each channel in its code's order.
        var plan = PlayFacts.OrbsOf(card);
        if (plan.Slots != 0) actions.Add(new OrbAction(OrbAction.Op.Slots) { Slots = plan.Slots });
        if (plan.EvokesEachOrb == 2) actions.Add(new OrbAction(OrbAction.Op.EvokeAllTwice));
        else if (plan.EvokesEachOrb > 0) actions.Add(new NotModelledAction($"every orb evoked {plan.EvokesEachOrb} times"));
        else if (plan.EvokesFront > 0) actions.Add(new OrbAction(OrbAction.Op.EvokeFront) { Times = plan.EvokesFront });
        // Aimed at no teammate (Ignition with none to aim at): nothing channelled.
        foreach (var (kind, times) in plan.Channels)
            if (channelTo != Who.None) actions.Add(new OrbAction(OrbAction.Op.Channel) { Orb = kind, Times = times, To = channelTo });
        var focus = FocusChange(card);
        if (focus != 0) actions.Add(new FocusAction(focus));
        return actions;
    }

    private static decimal SafeDecimal(Func<decimal> read)
    {
        try { return read(); } catch { return 0m; }
    }
}

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Procs;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

public static partial class SimAdapter
{
    /// <summary>
    /// The play of <paramref name="card"/> aimed at <paramref name="targetId"/> (docs/SIMULATOR.md "Card scripts"): the card play with what
    /// one play does as actions, each built from a step of the card's program (decision #193: SimAdapter.Steps), its values from the step's
    /// arguments, the models and the captured terms. <paramref name="owner"/>: whose card it is (a teammate's auto-play: their Howl from
    /// Beyond); <paramref name="me"/> is then that teammate, and what the play gives its player goes to them.
    /// </summary>
    /// <remarks>A member it couldn't read (Sts2State.ReadScope) makes the play unknown (UnreadAction), named by the member.</remarks>
    /// <param name="plays">The times the card plays (CardPlays.Count, the game's play-count hook); null: as it would now.</param>
    public static SimPlay Play(CardModel card, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int owner = Who.You, int? plays = null)
    {
        using var reads = Sts2State.TrackReads();
        var play = PlayFromGame(card, me, combat, ids, targetId, owner, plays ?? CardPlays.Count(card));
        if (reads.Unread.Count > 0) play.Before.Insert(0, new UnreadAction(reads.Unread[0]));
        return play;
    }

    private static SimPlay PlayFromGame(CardModel card, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int owner, int playCount, bool withUpgraded = true)
    {
        var name = card.GetType().Name;
        // The plays the simulator doesn't decide itself: Echo Form's it decides as the play starts (CardPlayAction.CountByModels).
        var aimedAt = ids.FirstOrDefault(entry => entry.Value == targetId).Key;
        var plays = Math.Max(1, Math.Max(1, playCount) - CardPlays.ModelledReplays(card, aimedAt));
        var play = new SimPlay
        {
            Name = name,
            IsAttack = card.Type == CardType.Attack,
            Plays = plays,
            // A card played from elsewhere (Howl from Beyond from the exhaust pile) doesn't leave the hand.
            // What its plays draw, make and take into your hand joins it as they run (decision #189); the cards its hooks bring back after.
            HandDelta = card.Pile?.Type is PileType.Exhaust or PileType.Draw ? 0 : -1,
            BroughtBack = ReturnToHand.Back(card, me, playCount).Count,
            TurnEndLeaving = card.Pile?.Type == PileType.Hand ? TurnEndOf(card) : null,
            FadingDelta = IsFading(card, combat) ? -1 : 0,
            HexFadingDelta = IsFading(card, combat) && EtherealOnlyFromHex(card) ? -1 : 0,
        };
        // Aimed at a teammate: what lands on them goes to them (never to you, even with no teammate to aim at).
        Creature? ally = null;
        var allyId = Who.None;
        if (card.TargetType == TargetType.AnyAlly && Who.TeammateIndex(targetId) is var aimedAtTeammate and >= 0)
        {
            var allies = Teammates(me, combat);
            if (aimedAtTeammate < allies.Count) { play.AllyTarget = aimedAtTeammate; ally = allies[aimedAtTeammate].Creature; allyId = Who.Teammate(aimedAtTeammate); }
        }
        play.HandCard = card.Pile?.Type == PileType.Hand ? HandId(card) : 0;
        // Where its own code sends it once played (decision #412: Mourning Blow's hand).
        (play.OwnResult, play.OwnResultUnknown) = OwnResultLocation(card, me, combat);
        // What it is in the pile it goes to once played (SimPlay.PileProfile, ExhaustProfile: the board read's, decision #392).
        if (play.HandCard != 0 && ids is BoardIds { ProfilesRead: true } board)
        {
            play.PileProfile = board.PlayedProfiles.GetValueOrDefault(card);
            play.ExhaustProfile = board.ExhaustedPlayedProfiles.GetValueOrDefault(card);
        }
        // Its draws join the hand as the simulator runs them (decision #189): none for the card leaving to account for.
        play.Draws = 0;
        // Whom its block and channels are for, read from its code (decision #182): a teammate it's aimed at (Lift, Ignition).
        play.BlockToAlly = Procs.Programs.CardEffects.BlocksAnother(card.GetType());
        play.ChannelToAlly = Procs.Programs.CardEffects.ChannelsForTarget(card.GetType());
        play.TargetId = card.TargetType == TargetType.AnyAlly ? allyId : targetId;
        play.Card = new SimCardPlay(WithEnchantmentBlock(SimReaders.Card(card, me), card, me, combat), owner, play.TargetId, PlayIndex: 0, plays, CardPlays.ChoiceAutoPlayed(card));
        // Osty makes its attack (an attack built FromOsty in its code).
        play.ByOsty = Procs.Programs.CardEffects.Attacks(card.GetType()).Any(attack => attack.Command.Builder.Contains("FromOsty"));

        // ---- what one play does, step by step from its program (decision #193)
        var aimedEnemy = card.TargetType == TargetType.AnyEnemy ? aimedAt : null;
        var steps = StepPlayOf(card, me, combat, ids, play, aimedEnemy, owner, ally, allyId, plays, boardTarget: aimedEnemy);
        play.Body = BodyFromSteps(steps);
        // A replay of a card whose numbers change with each play in a way its code doesn't show (decision #160: Thrash).
        // On any later play, the simulator's own replays (Echo Form's, decided as the play starts) too.
        if (PlayGrowth.Of(card).Unmodelled is { } growthUnknown) play.Body.Add(new FirstPlayOnly(Array.Empty<SimAction>()) { LaterPlays = new SimAction[] { new NotModelledAction(growthUnknown) } });
        if (DrawnCardReaderOf(card.GetType()) is { } reader) UseDrawnCard(play, reader);
        // The card's energy from its own hooks during the play (Epic Retrieval's for each arrow it knocks loose: CardEnergy.Rules).
        CardEnergy.AddHookEnergy(play, card);
        AddCardEvents(play, card);
        // What it turns into once its last play is over (Mourning Blow's Sadness).
        play.After.AddRange(MadeAfterPlay(card));
        // A teammate's card: the block it gives its player is theirs.
        if (owner != Who.You) play.Body = play.Body.Select(action => ToOwner(action, owner)).ToList();
        // A card upgraded during its play (CardCmd.Upgrade of the card in play) plays its later plays upgraded: read from its upgraded copy.
        if (withUpgraded && playCount > 1 && Safe(() => card.IsUpgradable) && UpgradesCardsInPlay(me)) play.Upgraded = UpgradedVersion(card, play, me, combat, ids, targetId, owner, playCount);
        return play;
    }

    /// <summary>
    /// The card <paramref name="card"/> turns into once its play is over (CardModel.TransformIntoAfterPlay, set in its OnPlay: Mourning Blow's Sadness, read
    /// as its AfterPlay make), as the game's canonical card, which is what lands in its result pile; null for one that doesn't, or whose card isn't built.
    /// </summary>
    private static CardModel? TurnsIntoAfterPlay(CardModel card)
    {
        try
        {
            return Procs.Programs.MadeCards.Of(card.GetType()).FirstOrDefault(make => make.By == Procs.Programs.MadeCards.Way.AfterPlay) is { Card: { } made }
                ? Procs.Programs.MadeCards.CanonicalOf(made) : null;
        }
        catch { return null; }
    }

    /// <summary>One of the player's powers or relics upgrades the card being played (SimModel.UpgradesCardInPlay, read from its code or its model).</summary>
    private static bool UpgradesCardsInPlay(Player me)
    {
        foreach (var relic in me.Relics) if (SimModels.Get(relic.GetType().Name) is { UpgradesCardInPlay: true }) return true;
        foreach (var power in me.Creature.Powers) if (SimModels.Get(power.GetType().Name) is { UpgradesCardInPlay: true }) return true;
        return false;
    }

    /// <summary>
    /// The card upgraded (CardCmd.Upgrade: UpgradeInternal, FinalizeUpgradeInternal on the card), read from an upgraded copy made the way the game
    /// makes its upgrade previews (MutableClone): what each later play does, and the card its hooks see (its values, keywords, tags and upgrade; the
    /// card play's own resources, its place in your hand and its result pile stay the card play's). Null when the copy can't be made.
    /// </summary>
    private static CardVersion? UpgradedVersion(CardModel card, SimPlay play, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, int targetId, int owner, int playCount)
    {
        if (play.Card is not { Card: var original }) return null;
        try
        {
            var copy = SpireQoL.Cards.UpgradedCopy.Of(card);
            var upgraded = PlayFromGame(copy, me, combat, ids, targetId, owner, playCount, withUpgraded: false);
            if (upgraded.Card is not { Card: var upgradedCard }) return null;
            return new CardVersion(original with
            {
                Upgraded = upgradedCard.Upgraded, Upgradable = upgradedCard.Upgradable, Vars = upgradedCard.Vars, Ethereal = upgradedCard.Ethereal, Tags = upgradedCard.Tags,
                Enchantment = upgradedCard.Enchantment, GainsBlock = upgradedCard.GainsBlock, EnchantmentGainsBlock = upgradedCard.EnchantmentGainsBlock, Targets = upgradedCard.Targets,
            }, upgraded.Body);
        }
        catch
        {
            return null;
        }
    }

    private static bool Safe(Func<bool> read)
    {
        try { return read(); }
        catch { return false; }
    }

    /// <summary>
    /// The card's enchantment (SimCard.Enchantment: the engine runs it after each play's own effects, from its class's code, decision #217) with the
    /// game's own block math for a block its code gains, for what the simulator doesn't model (its base the enchantment's Block value, where it has one).
    /// </summary>
    private static SimCard WithEnchantmentBlock(SimCard simCard, CardModel card, Player me, ICombatState combat)
    {
        if (simCard.Enchantment is not { } enchantment || card.Enchantment is not { } gameEnchantment) return simCard;
        var baseBlock = 0m;
        try { if (gameEnchantment.DynamicVars.TryGetValue("Block", out var blockVar)) baseBlock = blockVar.BaseValue; } catch { /* none */ }
        return simCard with { Enchantment = enchantment with { BlockTerms = SimReaders.Block(combat, card, me.Creature, baseBlock, ValueProp.Move) } };
    }

    /// <summary>
    /// The cards a card's enchantment draws when the card is played now, read from its OnPlay (Swift's Amount while it's on: ReadModel.DrawsOnPlay),
    /// for the cards that come back to your hand (the Return tags' hand size, outside the simulation). 0 before the models are read.
    /// </summary>
    internal static int EnchantmentDraws(CardModel card) =>
        card.Enchantment is { } enchantment && SimModels.Loaded && SimModels.Enchantment(enchantment.GetType().Name) is { } read && SimReaders.EnchantedCard(card) is { } simCard
            ? read.DrawsOnPlay(simCard) : 0;

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
            play.Body.RemoveAll(simAction => simAction is CardEventAction { Event: CardEvent.Discarded } or TakeFromHandAction { Event: CardEvent.Discarded });
            action = action with { DiscardUnlessFree = true };
        }
        play.Body[play.Body.FindIndex(simAction => simAction is CardEventAction { Event: CardEvent.Drawn })] = action;
    }

    /// <summary>
    /// The card events of the card play around its plays: the energy it spends and gives back (before the play), the stars it spends (the
    /// first play's first action, so Mini Regent's Strength is on the card's own hits) and its own exhaust (after the last play: it goes to
    /// its result pile once). A shuffle is its draws' or its own code's, in the simulation (decision #193).
    /// </summary>
    private static void AddCardEvents(SimPlay play, CardModel card)
    {
        if (CardPlays.EnergySpentOn(card) is > 0 and var energySpent) play.Before.Add(new CardEventAction(CardEvent.EnergySpent, energySpent));
        // Spent with the energy, before the card does anything (PlayCardAction: SpendResources, then OnPlay). Inside the play, as Galactic
        // Dust and Child of the Stars read its stars.
        if (CardPlays.StarsOf(card) > 0) play.Body.Insert(play.Body.TakeWhile(action => action is AskAction).Count(), new CardEventAction(CardEvent.StarsSpent, Count: 1) { FirstPlayOnly = true });
        // Its own exhaust (a model that sends it to the exhaust pile, Corruption, is the simulator's: decision #192).
        if (play.Card!.Card.ExhaustsSelf) play.After.Add(new CardEventAction(CardEvent.Exhausted, Count: 1) { Card = play.Card!.Card });
        // Its own exhaust sets off its own AfterCardExhausted (Drum of Battle's energy).
        if (play.Card!.Card.ExhaustsSelf && play.Card.Card.ExhaustEnergy is > 0 and var ownExhaustEnergy)
            play.After.Add(new CardEventAction(CardEvent.EnergyGained, ownExhaustEnergy) { Player = play.Card!.Owner });
    }
    /// <summary>
    /// The filtered takes from your hand of a card's play (TakesFromHand's keys, in its order), each with the test a card in your hand
    /// passes to be taken: what a card joining your hand during the play counts for (SimDrawCard.TakenBy, CardEventAction.TakenBy).
    /// </summary>
    internal static List<(string Key, Func<CardModel, bool, bool?> Takes)> TakeFiltersOf(AbstractModel model)
    {
        var type = model.GetType();
        var program = Procs.Programs.CardPrograms.Of(type);
        var board = Procs.Programs.ProgramBoard.For(model);
        var filters = new List<(string, Func<CardModel, bool, bool?>)>();
        foreach (var (take, key, _, eventIfNot, _) in Procs.Programs.BoardCounts.TakesInOrder(model))
            switch (take.Way)
            {
                case Procs.Programs.CardEffects.TakeWay.Picked when take.PickPile is null or MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand:
                    if (!take.ItemTest.IsAlways || eventIfNot != null) filters.Add((key, (other, made) => Procs.Programs.ProgramEvaluator.HoldsFor(Tests(take.ItemTest, made), other, board, program)));
                    break;
                case Procs.Programs.CardEffects.TakeWay.Random or Procs.Programs.CardEffects.TakeWay.Each:
                    if (Procs.Programs.CardEffects.IsYourWholeHand(take.Items) && take.ItemTest.IsAlways) break;
                    filters.Add((key, (other, made) => (take.Filter is { } filter ? Procs.Programs.ProgramEvaluator.Passes(new Procs.Programs.LambdaValue(filter.Parameter, Tests(filter.Body, made)), other, board, program) : true) is var passes
                                              && Procs.Programs.ProgramEvaluator.HoldsFor(Tests(take.ItemTest, made), other, board, program) is var holds
                                              && passes != false && holds != false ? (passes == null || holds == null ? null : true) : false));
                    break;
            }
        // And the filters its conditions ask of your hand (decision #190: Impatience's Attacks), followed the same way.
        foreach (var filter in Procs.Programs.ConditionReader.HandFiltersOf(type, program))
            filters.Add((filter.Key, (other, _) => Procs.Programs.ProgramEvaluator.Passes(filter.Lambda, other, board, program)));
        return filters;
    }

    /// <summary>
    /// The filtered takes a card the play makes would be taken by (each of the card's CreateSteps, read on its canonical card), every
    /// pile's take as well as its hand's share; null when what it makes can't be told (a helper's cards) or a test can't be read on it.
    /// </summary>
    private static IReadOnlyList<string>? MadeTakenBy(AbstractModel model)
    {
        var filters = TakeFiltersOf(model);
        if (filters.Count == 0) return Array.Empty<string>();
        if (MadeCardOf(model) is not { } madeCard) return null;
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

    /// <summary>The one card the play makes, the game's own (its program's CreateSteps all make it); null for none, several or one not read.</summary>
    internal static CardModel? MadeCardOf(AbstractModel model)
    {
        var made = Procs.Programs.CardPrograms.Of(model.GetType()).All().OfType<Procs.Programs.CreateStep>().Select(create => create.Card).Distinct().ToList();
        return made.Count == 1 ? ModelDb.AllCards.FirstOrDefault(canonical => canonical.GetType().Name == made[0]) : null;
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
    private static int DrawPileOf(AbstractModel model)
    {
        try { return Procs.Programs.BoardCounts.OwnerOf(model) is { } owner ? MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw.GetPile(owner).Cards.Count : 0; } catch { return 0; }
    }
    /// <summary>
    /// The attack's hit count's ways when its code works it out on a test the simulator asks (WithHitCount's argument a value two ways
    /// leave: Spite's {not LostHpThisTurn: 1; LostHpThisTurn: HitCount}): each way's condition (after the ways before it fail, as the first
    /// way that holds gives the value) and its hits, the card's own number. Null when it isn't such a count.
    /// </summary>
    private static List<(SimCondition Condition, int Hits)>? HitWaysOf(CardModel card, Procs.Programs.CommandStep attackStep)
    {
        var value = attackStep.HitCountValue;
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
    /// <summary>How a card's play takes cards into your hand (HandTakeOf).</summary>
    /// <param name="Piles">The piles it takes them from (your draw pile, your discard pile; both for a collection over every pile).</param>
    /// <param name="Eligible">Which cards there it can take.</param>
    /// <param name="Random">The pick is a roll (TakeRandom); else it's yours.</param>
    /// <param name="AllAtOnce">Every card it takes, on its first play (BoardCounts.TakenToHandCount); else one a play.</param>
    internal sealed record HandTake(IReadOnlyList<PileType> Piles, Func<CardModel, bool> Eligible, bool Random = false, bool AllAtOnce = false);

    /// <summary>
    /// How the card's play takes cards into your hand, read from its program (CardEffects.TakesIntoHandOf: its CardPileCmd.Add to the
    /// hand), so the cards it takes leave their pile in the simulation (TakeToHandAction): a pick from a pile, of the cards its filter
    /// lets through (Secret Weapon's Attacks), one a play unless it picks more (Dredge); a roll from a collection (Anointed's Rares, The
    /// World's discard pile), or each card of one (All For One's 0-cost cards, Summon Forth's Sovereign Blades from every pile), all on
    /// the first play. Null when it takes none, or you choose how many (Neow's Fury), or what it takes from can't be read.
    /// </summary>
    internal static HandTake? HandTakeOf(AbstractModel model)
    {
        var takes = Procs.Programs.CardEffects.TakesIntoHandOf(model.GetType());
        if (takes.Count == 0 || takes.Any(take => take.ChoosesHowMany)) return null;
        var take = takes[0];
        var program = Procs.Programs.CardPrograms.Of(model.GetType());
        var board = Procs.Programs.ProgramBoard.For(model);
        switch (take.Way)
        {
            case Procs.Programs.CardEffects.TakeWay.Picked when take.PickPile is { } pile && pile != PileType.Hand:
                return new HandTake(new[] { pile }, other => (take.PickFilter is not { } filter || Procs.Programs.ProgramEvaluator.Passes(filter, other, board, program) != false)
                                                             && Procs.Programs.ProgramEvaluator.HoldsFor(take.ItemTest, other, board, program) != false,
                    AllAtOnce: take.PerPick is not Procs.Programs.ConstantValue { Value: 1 });
            case Procs.Programs.CardEffects.TakeWay.Random or Procs.Programs.CardEffects.TakeWay.Each when take.Items != null
                                                                                                       && Procs.Programs.ProgramEvaluator.Items(take.Items, board, program) is { } items:
            {
                var cards = items.OfType<CardModel>().Where(other => !ReferenceEquals(other, model)).ToHashSet(ReferenceEqualityComparer.Instance);
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
    /// take, by profile (the board read's own: BoardIds), as a TakeToHandAction whose cards join your hand as they come (into a full hand, the
    /// discard pile). How many a play takes: a pick's most, a roll's count (read as it runs when it counts your hand's room: The World, Anointed),
    /// every card of a collection (All For One; a later play finds them gone). A play translated before its board's piles are read (a hand card's
    /// auto-play, a pile card's play, built with the board) can't name them: unknown where there's a card it could take.
    /// </summary>
    private static IEnumerable<SimAction> TakesToHand(AbstractModel model, Player me, IReadOnlyDictionary<Creature, int> ids)
    {
        if (HandTakeOf(model) is not { } take) yield break;
        var (count, from) = Procs.Programs.BoardCounts.HandTakeCount(model);
        if (DrawProfilesOf(ids) is not { } profiles)
        {
            if (take.Piles.Any(pile => pile is PileType.Draw or PileType.Discard && pile.GetPile(me).Cards.Any(take.Eligible)))
                yield return new NotModelledAction("a take from your piles in a play built before the board's piles are read");
            yield break;
        }
        foreach (var pile in take.Piles)
        {
            if (pile is not (PileType.Draw or PileType.Discard)) continue;
            var counts = Eligible(me, pile == PileType.Discard, take.Eligible, profiles);
            // A play that shuffles your discard pile in first when your draw pile is short (Ki Chant) can take from what it shuffles in.
            if (pile == PileType.Draw && Procs.Programs.CardEffects.ShufflesWhenShort(model.GetType()))
                foreach (var (key, inDiscard) in Eligible(me, fromDiscard: true, take.Eligible, profiles)) counts[key] = counts.GetValueOrDefault(key) + inDiscard;
            if (counts.Count == 0) continue;
            yield return new TakeToHandAction(take.Piles.Count > 1 ? int.MaxValue : count, counts)
                { Random = take.Random, FromDiscard = pile == PileType.Discard, JoinsHand = true, CountFrom = from };
        }
    }

    /// <summary>How many cards of each profile in your draw or discard pile pass the filter.</summary>
    private static Dictionary<string, int> Eligible(Player me, bool fromDiscard, Func<CardModel, bool> eligible, IReadOnlyDictionary<CardModel, SimDrawCard> profiles)
    {
        var counts = new Dictionary<string, int>();
        foreach (var other in (fromDiscard ? PileType.Discard : PileType.Draw).GetPile(me).Cards)
            if (eligible(other) && profiles.TryGetValue(other, out var profile)) counts[profile.Key] = counts.GetValueOrDefault(profile.Key) + 1;
        return counts;
    }
    private static decimal SafeDecimal(Func<decimal> read)
    {
        try { return read(); } catch { return 0m; }
    }
}

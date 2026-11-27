using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Procs;
using SpireQoL.Sim;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Card code against what the mod counts for it: reads every card's OnPlay from the compiled game
/// (its async state machine included) and checks each effect the mod predicts from per-card tables
/// against those tables, through the same functions the predictions use (the program's board counts BoardCounts, MadeCards and CardEffects, and SimAdapter) on
/// the game's own card instances (ModelDb.AllCards). A card that draws but isn't counted as drawing,
/// channels orbs the simulator doesn't add, gains block without a Block value, and so on, is listed;
/// CoverageWatch reports it when the card shows up in a fight. New cards and changed cards in a game
/// update are caught the same way, before anyone plays them.
/// </summary>
internal static class CardCoverage
{
    private static Dictionary<string, List<string>>? _issues;

    /// <summary>Card class name → what its code does that the mod doesn't count.</summary>
    public static IReadOnlyDictionary<string, List<string>> Issues => _issues ??= Build();

    private static Dictionary<string, List<string>> Build()
    {
        var result = new Dictionary<string, List<string>>();
        List<CardModel> cards;
        try { cards = ModelDb.AllCards.ToList(); }
        catch { return result; }
        var others = cards.Take(3).ToList();
        foreach (var card in cards)
        {
            try
            {
                var issues = Check(card, others, cards);
                if (issues.Count > 0) result[card.GetType().Name] = issues;
            }
            catch (Exception ex)
            {
                result[card.GetType().Name] = new List<string> { $"couldn't check: {ex.Message}" };
            }
        }
        return result;
    }

    /// <summary>
    /// A count its code takes from a calculated value of the card (a CalculatedVar: its multiplier reads the fight: Compile Driver's and
    /// Steady Cadence's draws, Voltaic's channels): worked out as the play runs (the game's own Calculate; the ledger: decision #191), and 0 on
    /// the game's template card, so the scan holds it to being such a value, not to a number.
    /// </summary>
    private static bool CalculatedFromTheFight(CardModel card, Procs.Programs.ProgramValue? value) =>
        value != null && value.Parts().Any(part => part is Procs.Programs.CardValue { Key: var key } && card.DynamicVars.TryGetValue(key, out var named) && named is CalculatedVar);

    /// <summary>
    /// Prefix of issues that only matter with teammates (multiplayer): CoverageWatch reports them only in a
    /// fight with other players, since alone the card acts on you, as the simulator models it.
    /// </summary>
    public const string TeammatesOnly = "with teammates: ";

    /// <summary>
    /// Cards aimed at players (TargetType AnyAlly or AllAllies), reviewed from their OnPlay: how the simulator handles
    /// what lands on the teammate (one simulation per teammate, Who.Teammate), or why it doesn't change this
    /// turn's outcome. A new ally card is reported until it has an entry.
    /// </summary>
    private static readonly Dictionary<string, string> TeammateTargetModelled = new()
    {
        // Modelled: what lands on the teammate goes to them.
        [nameof(Lift)] = "block to the teammate (CardEffects.BlocksAnother, from its code)",
        [nameof(Constellation)] = "block to the teammate (CardEffects.BlocksAnother); its draws and energy are theirs (CardEffects.DrawsForTarget): their draws are their card events, for their draw procs",
        [nameof(DemonicShield)] = "HP loss to you, block (worked out on the teammate) to them (CardEffects.BlocksAnother)",
        [nameof(Ignition)] = "a Plasma orb into the teammate's orbs, evoking their front orb when full (CardEffects.ChannelsForTarget)",
        [nameof(Mimic)] = "block to you equal to the teammate's block (CalculatedBlock on the target)",
        [nameof(Rally)] = "its block to every living player of your side (GetTeammatesOf: you too), worked out on each (SimAdapter.BlocksFrom)",
        [nameof(HuddleUp)] = "every living player of your side draws (GetTeammatesOf: you too): yours are simulated, a teammate's (as many as their hand and piles allow) are their card events (SimAdapter.DrawsFrom)",
        [nameof(BladeSymphony)] = "Shivs into every living player's hand (GetTeammatesOf: you too), each made by its owner: their created-card events (SimAdapter.MadeForPlayers)",
        [nameof(GlimpseBeyond)] = "Souls into every living player's draw pile (GetTeammatesOf: you too), created by you: your created-card events, per Soul (SimAdapter.MadeForPlayers)",
        // LegionOfBone.OnPlay: OstyCmd.Summon for every living player of your side.
        [nameof(LegionOfBone)] = "summons every living player's Osty (GetTeammatesOf: you too), each through OstyCmd.Summon's steps (SimAdapter.SummonsFrom, SimEngine.Summon)",
        // Reviewed: nothing in this turn's outcome (the end of turn and the enemy turn).
        [nameof(BelieveInYou)] = "energy for the teammate",
        [nameof(Blaze)] = "Strength for the teammate: their attacks, not your end of turn",
        // Cards for every player whose effect on teammates is on their own later plays (not simulated: their hands, #25).
        [nameof(OneForAll)] = "OneForAllPower on every player: damage on each one's own 0-cost attacks (ModifyDamageAdditive), yours counted",
        [nameof(Yoga)] = "Stretch on every player: each one's next block card (yours counted)",
        [nameof(Plot)] = "DrawCardsNextTurnPower on each teammate: their next turn's draw (ModifyHandDraw)",
        [nameof(Concoct)] = "ConcoctPower on the teammate: poison from their attacks this turn (AfterDamageGiven), removed at end of turn",
        [nameof(Coordinate)] = "temporary Strength for the teammate (CoordinatePower: TemporaryStrengthPower)",
        [nameof(Enrapture)] = "Vigor and Stretch for the teammate: their next attack and block card",
        [nameof(Fade)] = "temporary Dexterity for the teammate (FadePower: TemporaryDexterityPower)",
        [nameof(ImitationLearning)] = "ImitationLearningPower on you, aimed at the teammate: copies their Power cards (their plays)",
        [nameof(Largesse)] = "a random colorless card for the teammate (a roll)",
        [nameof(Soulbound)] = "SoulboundPower on the teammate: acts on cards generated later",
        [nameof(Tutor)] = "the teammate picks a card from their draw pile into their hand",
        // SelfDeprecate.OnPlay (AllAllies): Sadness into your hand, Joy into every other living player's hand, all created by
        // you (AddGeneratedCardsToCombat creator = you: your created-card procs, counted from its make step: MadeCards).
        // Joy has no hooks (energy and a draw when they play it), and the generated-card hooks only react to their creator.
        [nameof(SelfDeprecate)] = "Joy cards into each teammate's hand, created by you: their plays (energy and a draw); your Sadness and created-card procs are counted",
        // Modelled: Covered zeroes powered attacks on the teammate and your InterceptPower multiplies those on you by covered players
        // + 1: the play sets both players' hits from the game's damage math (SimAdapter.Play). Block to you is modelled.
        [nameof(Intercept)] = "block to you; the teammate's powered attacks move onto you: theirs × 0, yours × (covered + 2) / (covered + 1)",
    };

    /// <summary>
    /// A card aimed at a teammate whose effect there the simulator doesn't model: the play check can't compare it.
    /// The coverage check reports the card.
    /// </summary>
    internal static bool TeammateTargetUnmodelled(CardModel card) =>
        card.TargetType == TargetType.AnyAlly && !TeammateTargetModelled.ContainsKey(card.GetType().Name);

    /// <summary>Cards reviewed as not needing what the scan finds, and why.</summary>
    private static readonly Dictionary<string, string> Reviewed = new()
    {
        [nameof(DeprecatedCard)] = "a placeholder card the game keeps for old saves (draws 1, removes itself from the deck)",
        // EnergySurge.OnPlay: PlayerCmd.GainEnergy for every living player (GetTeammatesOf includes you), nothing else.
        [nameof(EnergySurge)] = "gives every player energy, which the simulator doesn't model for anyone (it predicts the turn's outcome, not what's played next)",
    };

    /// <summary>
    /// An upgraded copy of the game's template card, made the way the game makes its upgrade tooltips
    /// (MutableClone, UpgradeInternal): some cards only do something when upgraded (Spinner+ channels Glass).
    /// Null when the card can't be upgraded.
    /// </summary>
    private static CardModel? UpgradedCopy(CardModel card)
    {
        try
        {
            if (card.MaxUpgradeLevel < 1) return null;
            var copy = SpireQoL.Cards.UpgradedCopy.Of(card);
            return copy;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> Check(CardModel card, List<CardModel> others, List<CardModel> all)
    {
        var name = card.GetType().Name;
        if (Reviewed.ContainsKey(name)) return new List<string>();
        var calls = OnPlayCalls(card.GetType());
        var issues = new List<string>();
        var upgraded = UpgradedCopy(card);
        // Counted when the card or its upgrade counts it.
        bool None(Func<CardModel, int> count) => count(card) == 0 && (upgraded == null || count(upgraded) == 0);
        bool Calls(string type, string method) => calls.Contains(type + "." + method);
        bool Applies<T>() => calls.Contains("PowerCmd.Apply<" + typeof(T).Name + ">");
        bool HasPowerVar<T>() => card.DynamicVars.Values.Any(dynamicVar => dynamicVar.GetType() is { IsGenericType: true } type
                                                                 && type.GetGenericTypeDefinition() == typeof(PowerVar<>) && type.GetGenericArguments()[0] == typeof(T));

        // Cards aimed at a teammate or at every player: the simulator applies the card's effects to you only.
        if (card.TargetType == TargetType.AnyAlly && !TeammateTargetModelled.ContainsKey(name))
            issues.Add(TeammatesOnly + "targets a teammate (the simulator applies its effects to you)");
        if (card.TargetType == TargetType.AllAllies && !TeammateTargetModelled.ContainsKey(name))
            issues.Add(TeammatesOnly + "acts on every player (the simulator applies its effects to you only)");

        if ((Calls("CardPileCmd", "Draw") || Calls("CardCmd", "DiscardAndDraw"))
            && None(otherCard => Procs.Programs.BoardCounts.CardsDrawn(otherCard, handCount: 5) + Procs.Programs.BoardCounts.CardsDrawn(otherCard, handCount: 1)) && !Procs.Programs.CardEffects.DrawsForTarget(card.GetType())
            && !Procs.Programs.CardEffects.DrawsForYou(card.GetType()).Any(draw => CalculatedFromTheFight(card, draw.Count)))
            issues.Add("draws cards (not counted: Shuffle Warning, draw procs)");
        // A card that takes block away: the simulator models a card taking its target's (Expose), read from its program.
        if (Calls("CreatureCmd", "LoseBlock") && !Procs.Programs.CardEffects.TakesTargetsBlock(card.GetType()))
            issues.Add("takes block away from something other than its target (the simulator doesn't remove it)");
        // A card that sets off poison during the play (Outbreak).
        if (Calls(nameof(PoisonPower), "Trigger") && !Procs.Programs.CardEffects.TriggersPoison(card.GetType()))
            issues.Add("sets off poison now (the simulator only ticks it at the start of the enemies' turn)");
        // A card that reacts to its own kills: a step on the kill is guarded by it (decision #190: Timber's attack on every enemy,
        // Sunder's energy), checked with every condition below; one that counts its kills (Echoing Slash's repeats) is RepeatPerKill.
        if (Calls("DamageResult", "get_WasTargetKilled") && !Procs.Programs.CardEffects.RepeatsPerKill(card.GetType())
            && !Procs.Programs.CardEffects.Find(Procs.Programs.CardPrograms.Of(card.GetType()), _ => true).Any(found => found.Condition.Literals.Any(literal => literal.Test.Parts().Any(part => part is Procs.Programs.QueryValue { Name: "WasTargetKilled" }))))
            issues.Add("reacts to its own kills outside a step's condition (not simulated)");
        // Every step on a condition the simulator acts on (decision #190): its condition translated, decided before the play, or a gap of
        // a reviewed kind (ConditionGapKinds).
        issues.AddRange(ConditionGaps(card.GetType()));
        // The cards a take goes over (an exhaust, discard, transform or take into your hand: a roll's or a loop's collection) are counted on the
        // board (Procs.Programs.BoardCounts.Taken): a collection built with a query the evaluator doesn't follow would be counted as your whole hand.
        var cardType = card.GetType();
        foreach (var take in Procs.Programs.CardEffects.ExhaustsOfOthers(cardType).Concat(Procs.Programs.CardEffects.Discards(cardType))
                     .Concat(Procs.Programs.CardEffects.Transforms(cardType)).Concat(Procs.Programs.CardEffects.TakesIntoHandOf(cardType)))
            if (take.Items != null && Procs.Programs.ProgramEvaluator.UnknownQueries(take.Items) is { Count: > 0 } unknown)
                issues.Add($"takes cards from a collection built with {string.Join(", ", unknown)}, which its program's evaluator doesn't follow (counted as your whole hand: Procs.Programs.BoardCounts.Taken)");
        // A draw on a condition (Impatience: no Attack in hand) is counted when its condition holds on the board (Procs.Programs.BoardCounts.CardsDrawn,
        // evaluated from its program): one the evaluator can't read (a test of something the play itself gives) isn't checked.
        if (Procs.Programs.CardEffects.DrawsForYou(card.GetType()).Any(draw => !Procs.Programs.ProgramEvaluator.CanEvaluate(draw.Condition)))
            issues.Add("draws only on a condition its program's evaluator can't read (not checked in Procs.Programs.BoardCounts.CardsDrawn)");
        // An attack on a condition, on a card that glows gold: is the glow that condition (Pact's End)? Its hits must say.
        // Its attack's hits are its program's (Procs.Programs.BoardCounts.HitsPerPlay): the attack's condition and hit count, evaluated on the board.
        if (Procs.Programs.CardEffects.Attacks(card.GetType()) is [var attack, ..]
            && (!Procs.Programs.ProgramEvaluator.CanEvaluate(attack.Condition)
                || attack.Command.HitCountValue is { } hitCount && hitCount.Parts().Any(part => part is Procs.Programs.UnknownValue or Procs.Programs.ResultValue)))
            issues.Add("attacks on a condition or with a hit count its program's evaluator can't read (its hits: Procs.Programs.BoardCounts.HitsPerPlay)");
        if (Calls("OrbCmd", "Channel") && !Procs.Programs.CardEffects.ChannelsKnownOrbs(card.GetType()))
            issues.Add("channels orbs (the simulator doesn't add them)");
        if (Calls("OrbCmd", "Channel") && None(Procs.Programs.BoardCounts.ChannelCount)
            && !Procs.Programs.CardEffects.OrbChannels(card.GetType()).Any(channel => channel.Loops.Any(loop => loop.While?.Literals.Any(literal => literal.Test is Procs.Programs.CompareValue compare && CalculatedFromTheFight(card, compare.Right)) == true)))
            issues.Add("channels orbs (not counted for Metronome)");
        if (Calls("CreatureCmd", "GainBlock") && Procs.Programs.CardEffects.BlockGains(card.GetType()).Count == 0)
            issues.Add("gains block its program doesn't show (not in the simulator or Juggernaut)");
        // Block more than once in one play (Evil Eye twice after an exhaust): each gain sets off Juggernaut and block reactions, counted
        // by its loops' rounds (Procs.Programs.BoardCounts.BlockGainCount), which must be readable.
        if (Procs.Programs.CardEffects.BlockGains(card.GetType()).Any(gain => gain.Loops.Any(loop => !Procs.Programs.CardEffects.RoundsReadable(loop))
                                                                              && !Procs.Programs.CardEffects.GainsForEachOfLoop(gain.Command)))
            issues.Add("gains block in a loop whose rounds can't be read (counted once: Procs.Programs.BoardCounts.BlockGainCount)");
        // A block card that applies Stretch keeps it for a replay's later plays (Cho Ki); the simulator spends it otherwise.
        if (Calls("CreatureCmd", "GainBlock") && calls.Contains("PowerCmd.Apply<StretchPower>") && !Procs.Programs.CardEffects.KeepsStretchForReplays(card.GetType()))
            issues.Add("gains block and applies Stretch (a replay's later plays keep it: not in the simulator)");
        // Whether a block card spends Stretch depends on its block being powered: read from its Block value, or from the props its code
        // gives its gain (Fisticuffs the damage it dealt as a Move, Entrench your block Unpowered).
        if (card.GainsBlock && Fool.BlockProps(card) == null
            && !Procs.Programs.CardEffects.BlockGains(card.GetType()).Any(gain => gain.Command.Values.Count > 2 && gain.Command.Values[2] is Procs.Programs.ConstantValue))
            issues.Add("gains block without a Block value (whether it spends Stretch isn't known: Stretch tab)");
        // A block gain's amount is a card value, a constant, or what its code reads as the gain runs (CardEffects.BlockBaseOf: the attack's dealt
        // damage, the gainer's block, the target's block): any other amount its code works out isn't followed.
        if (Procs.Programs.CardEffects.BlockGains(card.GetType()).Any(gain => Procs.Programs.CardEffects.BlockBaseOf(gain.Command, card.GetType()) == SimBlockBase.Given
                                                                             && gain.Command.Values.Count > 1 && !BlockAmountGiven(gain.Command.Values[1])))
            issues.Add("gains block of an amount its code works out that the simulator doesn't read (a card value, the attack's dealt damage, your block or the target's)");
        // A card that isn't an attack but damages its target (Capture Spirit), read from its program: one damaging anything else isn't modelled.
        if (card.Type != CardType.Attack && Calls("CreatureCmd", "Damage") && card.DynamicVars.ContainsKey("Damage") && !Procs.Programs.CardEffects.DamagesTargetWithoutAttacking(card.GetType()))
            issues.Add("damages an enemy without attacking (not in the simulator: kills, lethal, Slippery)");
        // A second damage value (Timber's hit on every enemy after a kill): each attack's and each damage's value is the one its step names.
        if (card.DynamicVars.Any(entry => entry.Key != "Damage" && entry.Value is DamageVar && !DamageNamedByAStep(card.GetType(), entry.Key)))
            issues.Add("has a second damage value no attack or damage of its play names (not in the simulator: kills, lethal)");
        // A card that sets an enemy's HP (Judgement kills at or below a threshold), read from its program: its target's.
        if (Calls("CreatureCmd", "SetCurrentHp") && !Procs.Programs.CardEffects.SetsTargetsHp(card.GetType()))
            issues.Add("sets a creature's HP (not in the simulator: kills, lethal)");
        // A card that takes cards from your hand and draws: which comes first decides the room in your hand, read from its program
        // (decision #182). One whose program doesn't show both can't be put in order.
        var takes = Calls("CardCmd", "Discard") || Calls("CardCmd", "Exhaust") || Calls("CardCmd", "DiscardAndDraw");
        var program = Procs.Programs.CardPrograms.Of(card.GetType());
        if (takes && (Calls("CardPileCmd", "Draw") || Calls("CardCmd", "DiscardAndDraw"))
            && !(program.Commands.Any(command => command.Command.Name is "Discard" or "Exhaust" or "DiscardAndDraw") && program.Commands.Any(command => command.Command.Name is "Draw" or "DiscardAndDraw")))
            issues.Add("takes cards from your hand and draws, and its program doesn't show which comes first (the hand limit)");
        // An X-cost attack: its hits are X (unless its count is one of the named exceptions).
        if (card.Type == CardType.Attack && card.EnergyCost.CostsX && !Procs.Programs.CardEffects.HitsXTimes(card.GetType()) && name is not (nameof(HeavenlyDrill) or nameof(Stardust)))
            issues.Add("an X-cost attack whose hits aren't counted as X (damage, lethal)");
        // An attack whose code sets no hit count but has a Repeat value: its hits would be read from Repeat (Ice Lance's
        // Repeat is its Frost channels). A card that really attacks Repeat times in a loop goes in Reviewed.
        if (card.Type == CardType.Attack && !Calls("AttackCommand", "WithHitCount") && !card.DynamicVars.ContainsKey("HitCount")
            && !Procs.Programs.CardEffects.AttacksOnceRepeatingSomethingElse(card.GetType())
            && (Sts2Mods.Core.Game.Sts2State.Var(card, "Repeat", fallback: 1) > 1 || (upgraded != null && Sts2Mods.Core.Game.Sts2State.Var(upgraded, "Repeat", fallback: 1) > 1)))
            issues.Add("an attack with a Repeat value and no hit count in its code (its hits would be read as Repeat: damage, lethal)");
        if (Applies<PoisonPower>() && !HasPowerVar<PoisonPower>())
            issues.Add("applies poison outside its power value (not in the simulator)");
        // Every game card as the hand, so any filter (attacks, Sadness, Shoot cards) finds something to exhaust.
        if (Calls("CardCmd", "Exhaust") && None(otherCard => Procs.Programs.BoardCounts.ExhaustOthers(otherCard, all, drawPile: 5)))
            issues.Add("exhausts cards (not counted: Feel No Pain, Dark Embrace...)");
        // How it picks them decides whether a card that acts when exhausted goes (Drum of Battle's energy).
        if (Calls("CardCmd", "Exhaust") && !None(otherCard => Procs.Programs.BoardCounts.ExhaustOthers(otherCard, all, drawPile: 5))
            && SimAdapter.ExhaustPickOf(card) == null && Procs.Programs.BoardCounts.ExhaustFilterTakesActingCard(card, all))
            issues.Add("exhausts other cards with a filter a card that acts when exhausted passes (Drum of Battle's energy when exhausted isn't followed)");
        if ((Calls("CardCmd", "Discard") || Calls("CardCmd", "DiscardAndDraw")) && None(otherCard => Procs.Programs.BoardCounts.DiscardCount(otherCard, others, Procs.Programs.BoardCounts.CardsDrawn(otherCard, handCount: 5))))
            issues.Add("discards cards (not counted: Tingsha, Tough Bandages...)");
        // The cards it makes are its makes' (MadeCards, from its program): each make's count must be readable on the board.
        if ((Calls("CardPileCmd", "AddGeneratedCardToCombat") || Calls("CardPileCmd", "AddGeneratedCardsToCombat") || Calls("CardPileCmd", "AddToCombatAndPreview"))
            && None(otherCard => Procs.Programs.MadeCards.Summary(otherCard).Made))
            issues.Add("creates cards (not counted: created-card procs)");
        if (Procs.Programs.MadeCards.Summary(card).Unread || upgraded != null && Procs.Programs.MadeCards.Summary(upgraded).Unread)
            issues.Add("creates cards in a count its program's evaluator can't read (counted as one: created-card procs)");
        // A card whose hooks (not its play) move a card (itself: Make It So, Right Hand Hand...) to a pile: it comes back to
        // your hand on something you do, which only ReturnToHand models (the return tabs, your hand size after).
        if (HookCalls(card.GetType()).Contains("CardPileCmd.Add") && !ReturnToHand.Modelled.Contains(name) && !ReturnToHand.NextTurn.Contains(name))
            issues.Add("moves a card from its own hooks (comes back to your hand? not counted: return tabs, hand size)");
        // A card that reads a hit's overkill: to deal it again to the other enemies (Rain Shot, Omnislice: damage, lethal), or as block or
        // Stretch from what its attack dealt (Fisticuffs, Blossom Shot), each read from the command its code gives it to.
        if (Calls("DamageResult", "get_OverkillDamage") && !Procs.Programs.CardEffects.OverkillReadsFollowed(card.GetType()))
            issues.Add("reads a hit's overkill (deals it again to the other enemies? not counted: damage, lethal)");
        // A card whose own play moves cards (CardPileCmd.Add): into your hand (Hologram, Wish: your hand size after), back out of it
        // onto your draw pile (Glimmer), or somewhere your hand doesn't see. Each is counted or reviewed by where it moves them.
        if (Calls("CardPileCmd", "Add") && !Procs.Programs.CardEffects.TakesIntoHand(card.GetType()) && !Procs.Programs.CardEffects.PutsBackFromHand(card.GetType())
            && !Procs.Programs.CardEffects.TakesWholeHand(card.GetType()) && !Procs.Programs.CardEffects.MovesCardsElsewhere(card.GetType()))
            issues.Add("moves cards with CardPileCmd.Add in its play (into or out of your hand? not counted: hand size after)");
        // A card counted as taking cards into your hand: the simulator takes them out of their pile (which pile, which cards).
        if (Procs.Programs.CardEffects.TakesIntoHand(card.GetType()) && !Procs.Programs.CardEffects.TakesIntoHandOf(card.GetType()).Any(take => take.ChoosesHowMany || take.PileReadable))
            issues.Add("takes cards into your hand without saying from which pile (they'd stay there in the simulation: later draws)");
        // A card that auto-plays cards from your draw pile: the simulator plays them (which pile position, how many).
        if (Calls("CardPileCmd", "AutoPlayFromDrawPile") && !Procs.Programs.CardEffects.AutoPlaysFromDrawPile(card.GetType()))
            issues.Add("auto-plays cards from your draw pile (not simulated: what they do)");
        // A card that plays itself from your draw pile's top from its own hooks (I Am Invincible as your turn ends): the simulator follows it
        // (SimAdapter.ActsFromTopOfDrawPile: its block from the top, and a pick that could put it there is yours, decision #410).
        if (HookCalls(card.GetType()).Contains("CardPileCmd.AutoPlayFromDrawPile") && !SimAdapter.ActsFromTopOfDrawPile(card))
            issues.Add("plays from your draw pile's top from its own hooks (not followed: what it does as your turn ends, a pick putting it there)");
        // A card made carries its class and type to the created-card hooks (All Is Well's Sadness, Smokestack's Status): one its code creates
        // that no make carries is a card those hooks can't tell.
        if (Procs.Programs.CardEffects.CreatedNotMade(card.GetType()) is [var notMade, ..])
            issues.Add($"creates a {notMade} no make of its carries (what it is isn't told to the created-card hooks: All Is Well, Smokestack)");
        // Forging creates the Sovereign Blade through ForgeCmd, not in the card's own code.
        if (Calls("ForgeCmd", "Forge") && !Procs.Programs.CardEffects.Forges(card.GetType()))
            issues.Add("forges (its Sovereign Blade isn't counted: created-card procs)");
        // Every game card as the hand, so any filter (statuses, attacks) finds something to transform.
        if ((Calls("CardCmd", "Transform") || Calls("CardCmd", "TransformTo") || Calls("CardCmd", "TransformToRandom"))
            && None(otherCard => Procs.Programs.BoardCounts.TransformCount(otherCard, all, drawPile: 5)))
            issues.Add("transforms cards (not counted: the proc check's created-card budget)");
        if ((Calls("OrbCmd", "AddSlots") || Calls("OrbCmd", "RemoveSlots")) && None(otherCard => Math.Abs(Procs.Programs.BoardCounts.OrbsOf(otherCard).Slots)))
            issues.Add("changes your orb slots (the simulator doesn't change them)");
        // A power the simulator acts on, its amount one the adapter reads (decision #282): an unread one is an unknown in the play.
        foreach (var unread in SimAdapter.PowerAmountsUnread(card))
            issues.Add($"applies {unread} (an amount the simulator doesn't read: an unknown)");
        // Osty attacks are read from the game's tag (Procs.Programs.BoardCounts.IsOstyAttack): a card attacking from Osty without it wouldn't be simulated.
        if (Calls("AttackCommand", "FromOsty") && !Procs.Programs.BoardCounts.IsOstyAttack(card))
            issues.Add("attacks from Osty without the OstyAttack tag (its hit isn't simulated: Procs.Programs.BoardCounts.IsOstyAttack)");
        if (Calls("CreatureCmd", "Kill") && !Procs.Programs.CardEffects.KillsOsty(card.GetType()))
            issues.Add("kills a creature (not in the simulator)");
        // A card that becomes another card once played (Mourning Blow into a Sadness): a created card.
        if (Calls("CardModel", "set_TransformIntoAfterPlay") && !Procs.Programs.MadeCards.Of(card.GetType()).Any(make => make.By == Procs.Programs.MadeCards.Way.AfterPlay))
            issues.Add("turns into another card after the play (not counted: created-card procs)");
        if (Calls("CardPileCmd", "Shuffle") && !Procs.Programs.CardEffects.ShufflesDiscardPileIn(card.GetType()) && !Procs.Programs.CardEffects.ShufflesWhenShort(card.GetType()))
            issues.Add("shuffles your discard pile (not counted: shuffle procs, Biiig Hug)");
        // The Fool (v0.113.0): a shot does nothing without an arrow, which the adapter knows by the Shoot tag; arrows
        // pulled out by the card itself count for Twine and Yah only for the cards listed.
        if ((Calls("ArrowCmd", "ShootArrowAtTarget") || Calls("ArrowCmd", "TryShootArrow")) && !Procs.Fool.IsShot(card))
            issues.Add("shoots an arrow without the Shoot tag (its damage is counted even with no arrow to shoot)");
        if (Calls("ArrowCmd", "DislodgeArrow") && !Procs.Programs.CardEffects.DislodgesArrows(card.GetType()))
            issues.Add("knocks arrows loose (not counted: Twine, Yah)");
        if (Calls("PlayerCmd", "GainStars") && Procs.Programs.BoardCounts.StarGainsOf(card) == 0
            && !Procs.Programs.CardEffects.Find(program, command => command.Command is { DeclaringType: "PlayerCmd", Name: "GainStars" })
                .Any(gain => PlayConditions.CodeReading(card.GetType(), gain.Condition).Way == Procs.Programs.ConditionWay.Simulated))
            issues.Add("gains stars (not counted for Black Hole)");
        return issues;
    }

    /// <summary>A block gain's amount the adapter reads whole: a card value (DynamicVars.Block, a calculated block), or a constant.</summary>
    private static bool BlockAmountGiven(Procs.Programs.ProgramValue amount) =>
        amount.Parts().All(part => part is Procs.Programs.CardValue or Procs.Programs.ConstantValue or Procs.Programs.StoredValue or Procs.Programs.NamedValue { Name: "this" or "each" }
                                   || part is Procs.Programs.QueryValue { Name: "DynamicVars" or "op_Implicit" or "BaseValue" or "IntValue" or "Calculate" }
                                   || part is Procs.Programs.QueryValue { Of: Procs.Programs.QueryValue { Name: "DynamicVars" } });

    /// <summary>
    /// An attack or a damage of the card's play deals this damage value: its step names it (decision #193: Timber's OnKillDamage on its
    /// attack at every enemy after a kill, SimAdapter.AttacksFrom).
    /// </summary>
    private static bool DamageNamedByAStep(Type cardType, string key) =>
        Procs.Programs.CardPrograms.Of(cardType).Commands.Any(command => (command.Command.FullName == "DamageCmd.Attack" || command.Command.FullName == "CreatureCmd.Damage")
                                                                         && command.Values.Any(value => value.Parts().OfType<Procs.Programs.CardValue>().Any(cardValue => cardValue.Key == key)));

    /// <summary>
    /// The kinds of test the simulator doesn't ask yet (decision #190), each reviewed: a step on one is decided on the board only where the play
    /// can't change what it reads (decision #311); one it can change is the board's answer for a first play that reads it before any command, and
    /// unknown (`?`) on a replay's later plays and anywhere else, never decided early; or the step's own action asks it as it runs. A test of a
    /// kind not here, on a step the simulator acts on, is flagged by the card scan.
    /// </summary>
    internal static readonly Dictionary<string, string> ConditionGapKinds = new()
    {
        ["a roll"] = "a card or a target a roll takes (NextItem, TakeRandom, a card made at random): the action rolls it, a branch per outcome, or makes a card the simulator doesn't name; never read before the roll",
        ["a command's result"] = "what an earlier command of the play gave (a drawn card, a shot's arrow, a made card, damage results): the action that follows it asks it where the simulator has one (Escape Plan's draw, a shot's NeedsArrow, Rain Shot's splash, an attack's kill); else unknown where it runs (decision #311)",
        ["the fight"] = "the fight going on, the card being in it, its room a combat room, a test hook (IsOverOrEnding, CombatState, CurrentRoom, a mock): holds in every simulated play, nothing a play changes",
        ["a taken card"] = "a test of each card a loop or a pick takes (The Hermit's Curse): the take's own filter (TakeFromHandAction), followed through the play",
        ["the target"] = "a test of the target the simulator doesn't ask yet (dead or alive, every power's own test): the board's for a first play that reads it before any command, else unknown where it runs (decision #311); its powers (HasPower, GetPowerAmount), its intent to attack and whether its death sets off Fatal (decision #317) are asked as the step runs; Judgement's HP and Expose's Artifact are asked by their actions",
        ["a count of the board"] = "a count the simulator doesn't keep or doesn't ask yet (your orbs, the enemies, your draw or discard pile): the board's for a first play that reads it before any command, else unknown where it runs (decision #311); your exhaust pile is asked as the step runs; a count the step's own action reads as it runs (your hand's room, the cards a take took) and an enemy to hit for an attack that finds its targets as it runs are the action's own (decision #316)",
        ["your powers"] = "a power of yours or a teammate's the code tests that the simulator doesn't ask (none known: Imitation Learning's power aimed at a player and Frantic Escape's enemy Sandpit aimed at you are asked as the step runs, decision #322): the board's for a first play that reads it before any command, else unknown where it runs (decision #311); your HasPower and GetPowerAmount (Cho Ki's Stretch, Sovereign Blade's Parry) are asked as the step runs",
        ["the reader"] = "a value the reader can't follow yet (a helper's local, an array's length): a play can change it as far as anyone can tell, so the board's for a first play that reads it before any command, else unknown where it runs (decision #311; docs/CARD_PROGRAMS.md A5)",
    };

    /// <summary>The kind of an unread test (ConditionGapKinds), by what it reads; null for a kind not reviewed.</summary>
    internal static string? ConditionGapKind(Procs.Programs.ProgramValue test)
    {
        var parts = test.Parts().ToList();
        bool Names(params string[] names) => parts.Any(part => part is Procs.Programs.QueryValue query && names.Contains(query.Name) || part is Procs.Programs.NamedValue named && names.Contains(named.Name));
        if (Names("NextItem", "TakeRandom", "StableShuffle", "GetDistinctForCombat")) return "a roll";
        if (Names("IsOverOrEnding", "CombatState", "CurrentRoom", "_owner", "_mockGeneratedCard", "_mockSelectedCard", "DeckVersion", "_deckVersion")) return "the fight";
        if (parts.Any(part => part is Procs.Programs.ResultValue)) return "a command's result";
        if (Names("each")) return "a taken card";
        if (parts.Any(part => part is Procs.Programs.QueryValue { Name: "Target", Of: Procs.Programs.NamedValue { Name: "cardPlay" } })) return "the target";
        if (Names("Orbs", "HittableEnemies", "GetCards", "Take") || parts.Any(part => part is Procs.Programs.QueryValue { Name: "GetPile" })) return "a count of the board";
        if (Names("Powers", "GetPowerAmount", "GetOwnerParryAmount")) return "your powers";
        if (parts.Any(part => part is Procs.Programs.UnknownValue) || Names("count", "creatures")) return "the reader";
        return null;
    }

    /// <summary>
    /// The card's steps on a condition that the simulator acts on (a command it translates: SimAdapter.CommandsTranslated) whose test it
    /// doesn't ask and whose kind isn't reviewed (ConditionGapKinds).
    /// </summary>
    internal static IEnumerable<string> ConditionGaps(Type cardType)
    {
        foreach (var found in Procs.Programs.CardEffects.Find(Procs.Programs.CardPrograms.Of(cardType), _ => true))
        {
            if (found.Condition.IsAlways || !SimAdapter.CommandsTranslated.ContainsKey(found.Command.Command.FullName)) continue;
            var reading = PlayConditions.CodeReading(cardType, found.Condition);
            if (reading.Way != Procs.Programs.ConditionWay.NotRead || reading.Unread is { } unread && ConditionGapKind(unread) != null) continue;
            yield return $"runs {found.Command.Command} on a test the simulator doesn't ask (decision #190): {reading.Reason}";
        }
    }

    private static readonly BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, List<Type>> Applied = new();

    /// <summary>The power types a card's OnPlay applies (PowerCmd.Apply&lt;T&gt;), read from its compiled code.</summary>
    public static IReadOnlyList<Type> PowersApplied(Type cardType) => Applied.GetOrAdd(cardType, type =>
    {
        var list = new List<Type>();
        foreach (var method in OnPlayMethods(type))
        {
            try
            {
                foreach (var ins in SpireQoL.Diagnostics.GameCode.Instructions(method))
                    if (ins.operand is MethodInfo { IsGenericMethod: true, Name: "Apply" } otherMethod && otherMethod.DeclaringType?.Name == "PowerCmd")
                        list.Add(otherMethod.GetGenericArguments()[0]);
            }
            catch
            {
                // unreadable: nothing found
            }
        }
        return list;
    });

    /// <summary>Every type nested in <paramref name="type"/>, at any depth (CardCode.NestedTypes).</summary>
    internal static IEnumerable<Type> NestedTypes(Type type) => CardCode.NestedTypes(type);

    /// <summary>OnPlay, its state machine and the local functions it declares (CardCode.OnPlayMethods).</summary>
    private static IEnumerable<MethodBase> OnPlayMethods(Type cardType) => CardCode.OnPlayMethods(cardType);

    /// <summary>"Type.Method" for every call in the card's own hook overrides (After*, Before*) and their async state machines.</summary>
    private static HashSet<string> HookCalls(Type cardType)
    {
        var set = new HashSet<string>();
        bool IsHook(string methodName) => methodName.StartsWith("After", StringComparison.Ordinal) || methodName.StartsWith("Before", StringComparison.Ordinal);
        var methods = cardType.GetMethods(All).Where(method => method.DeclaringType == cardType && IsHook(method.Name)).Cast<MethodBase>()
            .Concat(NestedTypes(cardType)
                .Where(type => typeof(IAsyncStateMachine).IsAssignableFrom(type) && type.Name.StartsWith('<') && IsHook(type.Name[1..]))
                .Select(type => type.GetMethod("MoveNext", All)).Where(method => method != null).Cast<MethodBase>());
        foreach (var method in methods)
        {
            try
            {
                foreach (var ins in SpireQoL.Diagnostics.GameCode.Instructions(method))
                    if (ins.operand is MethodInfo otherMethod && otherMethod.DeclaringType != null) set.Add(otherMethod.DeclaringType.Name + "." + otherMethod.Name);
            }
            catch
            {
                // unreadable method: nothing found
            }
        }
        return set;
    }

    /// <summary>"Type.Method" (and "PowerCmd.Apply&lt;T&gt;") for every call in the card's play (CardCode.OnPlayCalls).</summary>
    private static IReadOnlySet<string> OnPlayCalls(Type cardType) => CardCode.OnPlayCalls(cardType);
}

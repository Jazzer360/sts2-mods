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
/// against those tables, through the same functions the predictions use (PlayFacts, SimAdapter) on
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
    /// Cards whose counts are calculated from the fight (orbs, statuses, history): handled in PlayFacts,
    /// but they read 0 on the game's template card, so the scan can't test them by value.
    /// Steady Cadence draws one per melody played this turn (MelodyCmd.MelodiesThisTurn of its owner).
    /// </summary>
    private static readonly HashSet<string> Calculated = new() { nameof(CompileDriver), nameof(FlakCannon), nameof(Voltaic), nameof(SteadyCadence) };

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
        [nameof(Rally)] = "its block to you and to every living teammate, worked out on each (CardEffects.BlocksEachPlayer)",
        [nameof(HuddleUp)] = "every living teammate draws, not you: their draws (as many as their hand and piles allow) are their card events (CardEffects.DrawsForEachTeammate)",
        [nameof(BladeSymphony)] = "Shivs into every living teammate's hand, created by them: their created-card events (SimAdapter.TeammatesShivCards)",
        [nameof(GlimpseBeyond)] = "Souls into every living teammate's draw pile, created by you: your created-card events, per Soul (PlayFacts.GeneratedCount)",
        // LegionOfBone.OnPlay: OstyCmd.Summon for every living player. Nothing reacts to a summon (no model overrides AfterSummon
        // or ModifySummonAmount, v0.113.1); it only raises each player's Osty, which is read from the game after the play.
        [nameof(LegionOfBone)] = "summons every player's Osty: nothing reacts to a summon, and each Osty's HP is read after the play",
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
        // you (AddGeneratedCardsToCombat creator = you: your created-card procs, counted in PlayFacts.GeneratedCount).
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

    /// <summary>
    /// Cards whose kill only gives a reward that changes nothing in the turn's outcome: Feed (max HP), Hand of Greed (gold),
    /// Knockout Blow (stars), Sunder (energy), The Hunt (a power for the fight's rewards).
    /// </summary>
    private static readonly HashSet<string> KillRewardCards = new() { nameof(Feed), nameof(HandOfGreed), nameof(KnockoutBlow), nameof(Sunder), nameof(TheHunt) };

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
            var copy = (CardModel)card.MutableClone();
            copy.UpgradeInternal();
            copy.FinalizeUpgradeInternal();
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
        if (Reviewed.ContainsKey(name) || Calculated.Contains(name)) return new List<string>();
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
            && None(otherCard => PlayFacts.CardsDrawn(otherCard, handCount: 5) + PlayFacts.CardsDrawn(otherCard, handCount: 1)) && !Procs.Programs.CardEffects.DrawsForTarget(card.GetType()))
            issues.Add("draws cards (not counted: Shuffle Warning, draw procs)");
        // A card that takes block away: the simulator models a card taking its target's (Expose), read from its program.
        if (Calls("CreatureCmd", "LoseBlock") && !Procs.Programs.CardEffects.TakesTargetsBlock(card.GetType()))
            issues.Add("takes block away from something other than its target (the simulator doesn't remove it)");
        // A card that sets off poison during the play (Outbreak).
        if (Calls(nameof(PoisonPower), "Trigger") && !SimAdapter.TriggerPoisonCards.Contains(name))
            issues.Add("sets off poison now (the simulator only ticks it at the start of the enemies' turn)");
        // A card that reacts to its own kills: more damage (Timber's hit on every enemy, Echoing Slash's repeats) is simulated;
        // a reward for the kill (energy, stars, gold, max HP, a power) changes nothing the turn's outcome shows.
        if (Calls("DamageResult", "get_WasTargetKilled") && !Procs.Programs.CardEffects.HitsAllOnKill(card.GetType())
            && !SimAdapter.RepeatPerKillCards.Contains(name) && !KillRewardCards.Contains(name))
            issues.Add("reacts to its own kills (not simulated)");
        // A draw on a condition (Impatience: no Attack in hand) is counted when its condition holds on the board (PlayFacts.CardsDrawn,
        // evaluated from its program): one the evaluator can't read (a test of something the play itself gives) isn't checked.
        if (Procs.Programs.CardEffects.DrawsForYou(card.GetType()).Any(draw => !Procs.Programs.ProgramEvaluator.CanEvaluate(draw.Condition)))
            issues.Add("draws only on a condition its program's evaluator can't read (not checked in PlayFacts.CardsDrawn)");
        // An attack on a condition, on a card that glows gold: is the glow that condition (Pact's End)? Its hits must say.
        if (PlayFacts.AttacksConditionallyAndGlows(card.GetType()) && !PlayFacts.GlowGatedAttackCards.Contains(name) && !PlayFacts.GlowNotTheAttack.ContainsKey(name))
            issues.Add("attacks only on a condition and glows gold (is the glow its condition? PlayFacts.GlowGatedAttackCards)");
        if (Calls("OrbCmd", "Channel") && !SimAdapter.HandlesChannel(name))
            issues.Add("channels orbs (the simulator doesn't add them)");
        if (Calls("OrbCmd", "Channel") && None(otherCard => PlayFacts.ChannelCount(otherCard, name, null!)))
            issues.Add("channels orbs (not counted for Metronome)");
        if (Calls("CreatureCmd", "GainBlock") && None(otherCard => PlayFacts.BlockGainCount(otherCard, name, others)))
            issues.Add("gains block without a Block value (not in the simulator or Juggernaut)");
        // Block more than once in one play (Evil Eye twice after an exhaust): each gain sets off Juggernaut and block reactions.
        if (Calls("CreatureCmd", "GainBlock") && RepeatsCall(card.GetType(), "CreatureCmd", "GainBlock") && !PlayFacts.RepeatedBlockCards.Contains(name))
            issues.Add("can gain block more than once in a play (a loop or two calls: counted once; PlayFacts.BlockGainCount needs a rule)");
        // A block card that applies Stretch keeps it for a replay's later plays (Cho Ki); the simulator spends it otherwise.
        if (Calls("CreatureCmd", "GainBlock") && calls.Contains("PowerCmd.Apply<StretchPower>") && !Procs.Programs.CardEffects.KeepsStretchForReplays(card.GetType()))
            issues.Add("gains block and applies Stretch (a replay's later plays keep it: not in the simulator)");
        // Whether a block card spends Stretch depends on its block being powered: read from its Block value, or listed.
        if (card.GainsBlock && Fool.BlockProps(card) == null && !Fool.BlockWithoutVar.ContainsKey(name))
            issues.Add("gains block without a Block value (whether it spends Stretch isn't known: Stretch tab)");
        // A card that isn't an attack but damages its target (Capture Spirit), read from its program: one damaging anything else isn't modelled.
        if (card.Type != CardType.Attack && Calls("CreatureCmd", "Damage") && card.DynamicVars.ContainsKey("Damage") && !Procs.Programs.CardEffects.DamagesTargetWithoutAttacking(card.GetType()))
            issues.Add("damages an enemy without attacking (not in the simulator: kills, lethal, Slippery)");
        // A second damage value (Timber's hit on every enemy after a kill): the simulator models the ones it knows.
        if (card.DynamicVars.Any(entry => entry.Key != "Damage" && entry.Value is DamageVar) && !Procs.Programs.CardEffects.HitsAllOnKill(card.GetType()))
            issues.Add("has a second damage value (not in the simulator: kills, lethal)");
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
        if (Calls("CardCmd", "Exhaust") && None(otherCard => PlayFacts.ExhaustOthers(otherCard, name, all, drawPile: 5, plays: 1)))
            issues.Add("exhausts cards (not counted: Feel No Pain, Dark Embrace...)");
        // How it picks them decides whether a card that acts when exhausted goes (Drum of Battle's energy).
        if (Calls("CardCmd", "Exhaust") && !None(otherCard => PlayFacts.ExhaustOthers(otherCard, name, all, drawPile: 5, plays: 1))
            && PlayFacts.ExhaustPickOf(card) == null && PlayFacts.ExhaustFilterTakesActingCard(card, all))
            issues.Add("exhausts other cards with a filter a card that acts when exhausted passes (Drum of Battle's energy when exhausted isn't followed)");
        if ((Calls("CardCmd", "Discard") || Calls("CardCmd", "DiscardAndDraw")) && None(otherCard => PlayFacts.DiscardCount(otherCard, name, others, PlayFacts.CardsDrawn(otherCard, handCount: 5))))
            issues.Add("discards cards (not counted: Tingsha, Tough Bandages...)");
        if ((Calls("CardPileCmd", "AddGeneratedCardToCombat") || Calls("CardPileCmd", "AddGeneratedCardsToCombat") || Calls("CardPileCmd", "AddToCombatAndPreview"))
            && None(otherCard => PlayFacts.GeneratedCount(otherCard, name, others, null!, out _)))
            issues.Add("creates cards (not counted: created-card procs)");
        // A card whose hooks (not its play) move a card (itself: Make It So, Right Hand Hand...) to a pile: it comes back to
        // your hand on something you do, which only ReturnToHand models (the return tabs, your hand size after).
        if (HookCalls(card.GetType()).Contains("CardPileCmd.Add") && !ReturnToHand.Modelled.Contains(name) && !ReturnToHand.NextTurn.Contains(name))
            issues.Add("moves a card from its own hooks (comes back to your hand? not counted: return tabs, hand size)");
        // A card that reads a hit's overkill: to deal it again to the other enemies (Rain Shot, Omnislice: damage, lethal) or for
        // something else, reviewed.
        if (Calls("DamageResult", "get_OverkillDamage") && !PlayFacts.SplashCards.Contains(name) && !PlayFacts.OverkillReaders.ContainsKey(name))
            issues.Add("reads a hit's overkill (deals it again to the other enemies? not counted: damage, lethal)");
        // A card whose own play moves cards (CardPileCmd.Add): into your hand (Hologram, Wish: your hand size after), back out of it
        // onto your draw pile (Glimmer), or somewhere your hand doesn't see. Each is counted or reviewed by where it moves them.
        if (Calls("CardPileCmd", "Add") && !Procs.Programs.CardEffects.TakesIntoHand(card.GetType()) && !Procs.Programs.CardEffects.PutsBackFromHand(card.GetType())
            && !Procs.Programs.CardEffects.TakesWholeHand(card.GetType()) && !Procs.Programs.CardEffects.MovesCardsElsewhere(card.GetType()))
            issues.Add("moves cards with CardPileCmd.Add in its play (into or out of your hand? not counted: hand size after)");
        // A card counted as taking cards into your hand: the simulator takes them out of their pile (which pile, which cards).
        if (Procs.Programs.CardEffects.TakesIntoHand(card.GetType()) && !SimAdapter.HandTakes.ContainsKey(name))
            issues.Add("takes cards into your hand without saying from which pile (they'd stay there in the simulation: later draws)");
        // A card that auto-plays cards from your draw pile: the simulator plays them (which pile position, how many).
        if (Calls("CardPileCmd", "AutoPlayFromDrawPile") && !Procs.Programs.CardEffects.AutoPlaysFromDrawPile(card.GetType()))
            issues.Add("auto-plays cards from your draw pile (not simulated: what they do)");
        // Sadness made by the card (Fertilizer, All Is Well react): its created statuses are counted as Sadness only for these.
        if (calls.Any(call => call.EndsWith(".CreateCard<Sadness>", StringComparison.Ordinal)) && !Procs.Programs.CardEffects.MakesSadness(card.GetType()))
            issues.Add("creates a Sadness (not counted: Fertilizer, All Is Well)");
        // Forging creates the Sovereign Blade through ForgeCmd, not in the card's own code.
        if (Calls("ForgeCmd", "Forge") && !Procs.Programs.CardEffects.Forges(card.GetType()))
            issues.Add("forges (its Sovereign Blade isn't counted: created-card procs)");
        // Every game card as the hand, so any filter (statuses, attacks) finds something to transform.
        if ((Calls("CardCmd", "Transform") || Calls("CardCmd", "TransformTo") || Calls("CardCmd", "TransformToRandom"))
            && None(otherCard => PlayFacts.TransformCount(otherCard, name, all, drawPile: 5)))
            issues.Add("transforms cards (not counted: the proc check's created-card budget)");
        if ((Calls("OrbCmd", "AddSlots") || Calls("OrbCmd", "RemoveSlots")) && None(otherCard => Math.Abs(SimAdapter.SlotChange(otherCard, name))))
            issues.Add("changes your orb slots (the simulator doesn't change them)");
        foreach (var applied in PowersApplied(card.GetType()))
            if (SimAdapter.PlainSelfPowerAmount(card, applied) == 0)
                issues.Add($"applies {applied.Name} with no amount the simulator reads");
        // Osty attacks are read from the game's tag (PlayFacts.IsOstyAttack): a card attacking from Osty without it wouldn't be simulated.
        if (Calls("AttackCommand", "FromOsty") && !PlayFacts.IsOstyAttack(card))
            issues.Add("attacks from Osty without the OstyAttack tag (its hit isn't simulated: PlayFacts.IsOstyAttack)");
        if (Calls("CreatureCmd", "Kill") && !Procs.Programs.CardEffects.KillsOsty(card.GetType()))
            issues.Add("kills a creature (not in the simulator)");
        // A card that becomes another card once played (Mourning Blow into a Sadness): a created card.
        if (Calls("CardModel", "set_TransformIntoAfterPlay") && None(otherCard => PlayFacts.GeneratedCount(otherCard, name, others, null!, out _)))
            issues.Add("turns into another card after the play (not counted: created-card procs)");
        if (Calls("CardPileCmd", "Shuffle") && !Procs.Programs.CardEffects.ShufflesDiscardPileIn(card.GetType()) && !Procs.Programs.CardEffects.ShufflesWhenShort(card.GetType()))
            issues.Add("shuffles your discard pile (not counted: shuffle procs, Biiig Hug)");
        // The Fool (v0.113.0): a shot does nothing without an arrow, which PlayFacts knows by the Shoot tag; arrows
        // pulled out by the card itself count for Twine and Yah only for the cards listed.
        if ((Calls("ArrowCmd", "ShootArrowAtTarget") || Calls("ArrowCmd", "TryShootArrow")) && !Procs.Fool.IsShot(card))
            issues.Add("shoots an arrow without the Shoot tag (its damage is counted even with no arrow to shoot)");
        if (Calls("ArrowCmd", "DislodgeArrow") && !Procs.Programs.CardEffects.DislodgesArrows(card.GetType()))
            issues.Add("knocks arrows loose (not counted: Twine, Yah)");
        if (Calls("PlayerCmd", "GainStars") && PlayFacts.StarGainsOf(card) == 0 && !Procs.Programs.CardEffects.GainsStarsOnKill(card.GetType()))
            issues.Add("gains stars (not counted for Black Hole)");
        return issues;
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

    /// <summary>
    /// The card's OnPlay calls Type.Method more than once, or inside a loop (a call between a backward branch and its target).
    /// </summary>
    private static bool RepeatsCall(Type cardType, string type, string method)
    {
        foreach (var onPlayMethod in OnPlayMethods(cardType))
        {
            try
            {
                var code = SpireQoL.Diagnostics.GameCode.Instructions(onPlayMethod);
                var at = Enumerable.Range(0, code.Count)
                    .Where(index => code[index].operand is MethodInfo nextMethod && nextMethod.Name == method && nextMethod.DeclaringType?.Name == type).ToList();
                if (at.Count > 1) return true;
                if (at.Count == 0) continue;
                var labels = new Dictionary<System.Reflection.Emit.Label, int>();
                for (var i = 0; i < code.Count; i++) foreach (var label in code[i].labels) labels[label] = i;
                for (var i = 0; i < code.Count; i++)
                    if (code[i].operand is System.Reflection.Emit.Label otherLabel && labels.TryGetValue(otherLabel, out var to) && to < i && at.Any(call => call >= to && call < i))
                        return true;
            }
            catch
            {
                // unreadable method
            }
        }
        return false;
    }

    /// <summary>"Type.Method" (and "PowerCmd.Apply&lt;T&gt;") for every call in the card's play (CardCode.OnPlayCalls).</summary>
    private static IReadOnlySet<string> OnPlayCalls(Type cardType) => CardCode.OnPlayCalls(cardType);
}

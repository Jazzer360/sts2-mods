namespace SpireQoL.Procs.Programs;

/// <summary>
/// What a card's play does, read from its program (decision #182): the game command and whom it acts on (an argument's owner:
/// "Owner.Creature" is you, "cardPlay.Target" the card's target). Each question is asked of the command, never of a card's name, so
/// a card added or changed in a game update is read the same way. Replaced per-card tables (KillsOstyCards, ExecuteCards, ...).
/// </summary>
public static class CardEffects
{
    private const string You = "Owner.Creature";
    private const string Target = "cardPlay.Target";

    public static bool KillsOsty(Type cardType) => KillsOsty(CardPrograms.Of(cardType));
    public static bool SetsTargetsHp(Type cardType) => SetsTargetsHp(CardPrograms.Of(cardType));
    public static bool TakesTargetsBlock(Type cardType) => TakesTargetsBlock(CardPrograms.Of(cardType));
    public static bool DamagesTargetWithoutAttacking(Type cardType) => DamagesTargetWithoutAttacking(CardPrograms.Of(cardType));
    public static bool BlocksAnother(Type cardType) => BlocksAnother(CardPrograms.Of(cardType));
    public static bool BlocksEachPlayer(Type cardType) => BlocksEachPlayer(CardPrograms.Of(cardType));
    public static bool ChannelsForTarget(Type cardType) => ChannelsForTarget(CardPrograms.Of(cardType));

    /// <summary>Kills your Osty (CreatureCmd.Kill on Owner.Osty: Sacrifice, Bone Shards).</summary>
    public static bool KillsOsty(ModelProgram program) => program.Commands.Any(command => Is(command, "CreatureCmd", "Kill") && Argument(command, 0) == "Owner.Osty");

    /// <summary>Sets its target's HP (CreatureCmd.SetCurrentHp: Judgement's execute).</summary>
    public static bool SetsTargetsHp(ModelProgram program) => program.Commands.Any(command => Is(command, "CreatureCmd", "SetCurrentHp") && Argument(command, 0) == Target);

    /// <summary>Takes its target's block (CreatureCmd.LoseBlock on the target: Expose).</summary>
    public static bool TakesTargetsBlock(ModelProgram program) => program.Commands.Any(command => Is(command, "CreatureCmd", "LoseBlock") && Argument(command, 1) == Target);

    /// <summary>Damages its target with CreatureCmd.Damage rather than an attack (Capture Spirit).</summary>
    public static bool DamagesTargetWithoutAttacking(ModelProgram program) =>
        program.Commands.Any(command => Is(command, "CreatureCmd", "Damage") && Argument(command, 1) == Target);

    /// <summary>Gains block for a creature that isn't you, outside a loop over players (Lift, Demonic Shield: its target; Constellation: the teammate it picked).</summary>
    public static bool BlocksAnother(ModelProgram program) =>
        Outside(program.Steps).Any(command => Is(command, "CreatureCmd", "GainBlock") && Argument(command, 0) is { } gainer && gainer != You
                                             && (gainer == Target || gainer.EndsWith(".Creature", StringComparison.Ordinal)));

    /// <summary>Gains block for each creature a loop goes over (Rally: every living player), not for you or the target each round.</summary>
    public static bool BlocksEachPlayer(ModelProgram program) =>
        program.All().OfType<LoopStep>().Any(loop => loop.Body.OfType<CommandStep>().Any(command => Is(command, "CreatureCmd", "GainBlock") && Argument(command, 0) is { } gainer
                                                                                                    && gainer != You && gainer != Target));

    /// <summary>Channels an orb for its target's player (Ignition: into a teammate's orbs).</summary>
    public static bool ChannelsForTarget(ModelProgram program) =>
        program.Commands.Any(command => Is(command, "OrbCmd", "Channel") && command.Arguments.Contains(Target + ".Player"));

    public static bool DrawsForTarget(Type cardType) => DrawsForTarget(CardPrograms.Of(cardType));
    public static bool DrawsForEachTeammate(Type cardType) => DrawsForEachTeammate(CardPrograms.Of(cardType));
    public static bool ShufflesDiscardPileIn(Type cardType) => ShufflesDiscardPileIn(CardPrograms.Of(cardType));
    public static bool ShufflesWhenShort(Type cardType) => ShufflesWhenShort(CardPrograms.Of(cardType));
    public static bool TakesIntoHand(Type cardType) => TakesIntoHand(CardPrograms.Of(cardType));
    public static bool PutsBackFromHand(Type cardType) => PutsBackFromHand(CardPrograms.Of(cardType));
    public static bool MovesCardsElsewhere(Type cardType) => MovesCardsElsewhere(CardPrograms.Of(cardType));

    // ---- draws and shuffles

    /// <summary>Draws for a player that isn't you, outside a loop over players (Constellation: CardPileCmd.Draw for its target's player).</summary>
    public static bool DrawsForTarget(ModelProgram program) =>
        Outside(program.Steps).Any(command => IsDraw(command) && Argument(command, 2) is { } player && player != "Owner");

    /// <summary>Draws for each player a loop goes over, not you (Huddle Up: every teammate draws).</summary>
    public static bool DrawsForEachTeammate(ModelProgram program) =>
        program.All().OfType<LoopStep>().Any(loop => loop.Body.OfType<CommandStep>().Any(command => IsDraw(command) && Argument(command, 2) is { } player && player != "Owner"));

    /// <summary>Shuffles your discard pile into your draw pile on every play (Reboot: CardPileCmd.Shuffle, no condition around it).</summary>
    public static bool ShufflesDiscardPileIn(ModelProgram program) =>
        program.Commands.Any(command => Is(command, "CardPileCmd", "Shuffle") && command.Condition == null);

    /// <summary>Shuffles your discard pile in only on a condition (Ki Chant: when your draw pile is short of what it looks at).</summary>
    public static bool ShufflesWhenShort(ModelProgram program) =>
        program.Commands.Any(command => Is(command, "CardPileCmd", "Shuffle")) && !ShufflesDiscardPileIn(program);

    private static bool IsDraw(CommandStep command) =>
        command.Command.DeclaringType == "CardPileCmd" && command.Command.Name.StartsWith("Draw", StringComparison.Ordinal);

    /// <summary>
    /// One draw of the play for you (CardPileCmd.Draw to its owner, or CardCmd.DiscardAndDraw), with everything it runs on (its own
    /// condition and every loop's around it) and the loops it's in.
    /// </summary>
    public sealed record DrawForYou(CommandStep Command, ProgramCondition Condition, IReadOnlyList<LoopStep> Loops)
    {
        /// <summary>How many one call draws: its count argument (Draw(choiceContext, player) draws one).</summary>
        public ProgramValue Count => Command.Command.Name == "DiscardAndDraw" ? Value(2) : Command.Command.ParameterCount == 2 ? new ConstantValue(1) : Value(1);

        private ProgramValue Value(int index) => index < Command.Values.Count ? Command.Values[index] : new UnknownValue("count");
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<DrawForYou>> Draws = new();

    /// <summary>The card type's draws for you, read once per type (its program doesn't change while the game runs).</summary>
    public static IReadOnlyList<DrawForYou> DrawsForYou(Type cardType) => Draws.GetOrAdd(cardType, type => DrawsForYou(CardPrograms.Of(type)));

    /// <summary>
    /// The play's draws for you, in order: to its owner, or to each player a loop over your side goes over (GetTeammatesOf: you are one of
    /// them, Huddle Up).
    /// </summary>
    public static IReadOnlyList<DrawForYou> DrawsForYou(ModelProgram program) =>
        Find(program, command => command.Command.DeclaringType == "CardCmd" && command.Command.Name == "DiscardAndDraw" || IsDraw(command))
            .Where(found => !IsDraw(found.Command) || found.Command.Arguments.Contains("Owner") || ForEachOfYourSide(found))
            .Select(found => new DrawForYou(found.Command, found.Condition, found.Loops.Where(loop => !OverYourSide(loop)).ToList())).ToList();

    /// <summary>The loop goes over each creature on your side (a foreach over CombatState.GetTeammatesOf: you and your teammates).</summary>
    public static bool OverYourSide(LoopStep loop) =>
        loop.Over.StartsWith("each of", StringComparison.Ordinal) && loop.Items?.Parts().Any(part => part is QueryValue { Name: "GetTeammatesOf" }) == true;

    /// <summary>The command acts on each player of a loop over your side (its argument is the loop's item: each.Player).</summary>
    private static bool ForEachOfYourSide(Found found) =>
        found.Loops.Any(OverYourSide) && found.Command.Values.Any(value => value.Parts().Any(part => part is NamedValue { Name: "each" }));

    // ---- cards a play takes: other cards exhausted, discarded, transformed or moved into your hand

    /// <summary>How a command of the play takes the cards it acts on.</summary>
    public enum TakeWay
    {
        /// <summary>Cards you pick (CardSelectCmd): Brand, Burning Pact, Purity, Cleanse from your draw pile, The Hermit's Curse, Acrobatics' discard.</summary>
        Picked,
        /// <summary>Cards a roll takes from a collection (NextItem: Cinder, Thrash; TakeRandom: Anointed, The World).</summary>
        Random,
        /// <summary>Each card of a collection (Fiend Fire your hand, Second Wind its non-Attacks, Storm of Steel your hand, Compact your statuses).</summary>
        Each,
        /// <summary>Cards the play drew, by what they are (Scrape: the ones that cost energy): at most what it drew.</summary>
        Drawn,
        /// <summary>
        /// The top cards of your draw pile it looks at, less the ones picked from them (Ki Chant: Except(Take(drawPile, count), picked)):
        /// how many it looks at, after the shuffle its play makes first when the pile is short.
        /// </summary>
        LookedAt,
    }

    /// <summary>
    /// One command of the play that takes cards (CardCmd.Exhaust, Discard, Transform, CardPileCmd.Add into your hand), with everything it
    /// runs on: how it takes them, what it goes over (the collection a loop, a roll or the command itself is over), the pick it takes them
    /// from, and the tests on the card taken (The Hermit: a Curse).
    /// </summary>
    public sealed record CardsTaken(CommandStep Command, ProgramCondition Condition, TakeWay Way, ProgramValue? Items, ChoiceStep? Pick)
    {
        /// <summary>At most one card of its collection (FirstOrDefault, Item(0): Punch of Joy's Sadness), not a pick.</summary>
        public bool Single { get; init; }

        /// <summary>A roll's count (TakeRandom's: Anointed your hand's room); null for one (NextItem).</summary>
        public ProgramValue? RollCount { get; init; }

        /// <summary>The pile a pick is from (CardSelectCmd.FromCombatPile's pile; FromHand and the like: your hand).</summary>
        public MegaCrit.Sts2.Core.Entities.Cards.PileType? PickPile => Pick == null ? null
            : Pick.Values.Select(Unstored).OfType<QueryValue>().FirstOrDefault(value => value is { Name: "GetPile", Arguments: [ConstantValue, ..] }) is { Arguments: [ConstantValue { Value: var pile }, ..] }
                ? (MegaCrit.Sts2.Core.Entities.Cards.PileType)pile
            : Pick.Command.Name.StartsWith("FromHand", StringComparison.Ordinal) ? MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand
            : null;

        /// <summary>
        /// Where it takes from can be read from its code: a pick from a pile, or a roll or a loop over a collection read whole (the card
        /// scan holds a card moving cards into your hand to it: SimAdapter.HandTakeOf takes them out of that pile).
        /// </summary>
        public bool PileReadable => Way == TakeWay.Picked ? PickPile is MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw or MegaCrit.Sts2.Core.Entities.Cards.PileType.Discard
            : Way is TakeWay.Random or TakeWay.Each && Items != null && !Items.Parts().Any(part => part is UnknownValue);

        /// <summary>The pick is from your draw pile (Cleanse: CardSelectCmd.FromCombatPile on PileType.Draw), not your hand.</summary>
        public bool FromDrawPile => PickPile == MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw;

        /// <summary>The pick's filter (FromCombatPile's last argument: Secret Weapon's Attacks), null when it has none.</summary>
        public LambdaValue? PickFilter => Pick?.Values.Count > 0 ? Pick.Values[^1] as LambdaValue : null;

        /// <summary>The selector the pick is made with (CardSelectorPrefs: its prompt, then how many: exactly, or at least and at most).</summary>
        private QueryValue? Prefs => Pick?.Values.OfType<QueryValue>().FirstOrDefault(value => value.Name == ".ctor" && value.Type == "CardSelectorPrefs");

        /// <summary>
        /// How many one pick takes at most: one for its first card (FirstOrDefault), else the selector's most (CardSelectorPrefs' last
        /// number: Purity's Cards).
        /// </summary>
        public ProgramValue PerPick => Command.Values.Count > 1 && Command.Values.Any(value => Unstored(value) is QueryValue { Name: "FirstOrDefault" }) || Pick == null
            ? new ConstantValue(1)
            : Prefs is { Arguments.Count: > 1 } prefs ? prefs.Arguments[^1] : new UnknownValue("pick size");

        /// <summary>
        /// You choose how many it takes (CardSelectorPrefs with a least and a most that differ: Guards' 0 to any, Neow's Fury's 0 to its
        /// Cards): what it takes is your later decision, not the play's.
        /// </summary>
        public bool ChoosesHowMany => Prefs is { Arguments.Count: >= 3 } prefs && !Equals(prefs.Arguments[1], prefs.Arguments[2]);

        /// <summary>The tests its condition makes on the card it takes (each: a loop's item), as a filter; the rest of its condition apart.</summary>
        public ProgramCondition ItemTest => Only(Condition, onItem: true);
        public ProgramCondition PlayTest => Only(Condition, onItem: false);

        /// <summary>What it takes from the collection it goes over (Items' Where lambda; null when it's all of it).</summary>
        public LambdaValue? Filter => Unwrapped(Items) is QueryValue { Name: "Where", Arguments: [_, LambdaValue lambda] } ? lambda : null;

        /// <summary>The top of your draw pile it looks at (Take(drawPile.Cards, count): its count), for LookedAt.</summary>
        public ProgramValue? Looks => Items?.Parts().OfType<QueryValue>().FirstOrDefault(value => value is { Name: "Take", Arguments.Count: 2 }) is { } take
            ? take.Arguments[1] : null;

        private static ProgramCondition Only(ProgramCondition condition, bool onItem)
        {
            if (condition.Terms.Count != 1) return onItem ? ProgramCondition.Always : condition;
            var kept = ProgramCondition.Always;
            foreach (var literal in condition.Terms[0])
                if (literal.Test.Parts().Any(part => part is NamedValue { Name: "each" }) == onItem) kept = kept.And(literal);
            return kept;
        }
    }

    /// <summary>A collection's value without what doesn't change its cards (ToList, a local holding it).</summary>
    private static ProgramValue? Unwrapped(ProgramValue? value) => value switch
    {
        QueryValue { Name: "ToList" or "ToArray" or "AsEnumerable", Arguments: [var inner] } => Unwrapped(inner),
        StoredValue stored => Unwrapped(stored.Value),
        _ => value,
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<CardsTaken>> Exhausts = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<CardsTaken>> DiscardTakes = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<CardsTaken>> TransformTakes = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<CardsTaken>> HandTakeSteps = new();

    public static IReadOnlyList<CardsTaken> ExhaustsOfOthers(Type cardType) => Exhausts.GetOrAdd(cardType, type => ExhaustsOfOthers(CardPrograms.Of(type)));
    public static IReadOnlyList<CardsTaken> Discards(Type cardType) => DiscardTakes.GetOrAdd(cardType, type => Discards(CardPrograms.Of(type)));
    public static IReadOnlyList<CardsTaken> Transforms(Type cardType) => TransformTakes.GetOrAdd(cardType, type => Transforms(CardPrograms.Of(type)));
    public static IReadOnlyList<CardsTaken> TakesIntoHandOf(Type cardType) => HandTakeSteps.GetOrAdd(cardType, type => TakesIntoHandOf(CardPrograms.Of(type)));

    /// <summary>
    /// The play's exhausts of other cards, in order: CardCmd.Exhaust of a pick's card (Picked), of a roll's (Random), or of each card a
    /// loop goes over (Each; a loop over a pick's result is Picked). An exhaust of the card itself, or of a card it can't tell, isn't one.
    /// </summary>
    public static IReadOnlyList<CardsTaken> ExhaustsOfOthers(ModelProgram program) =>
        Takes(program, command => Is(command, "CardCmd", "Exhaust") ? 1 : null, collections: false);

    /// <summary>The play's discards (CardCmd.Discard, DiscardAndDraw: the cards are their second argument), in order.</summary>
    public static IReadOnlyList<CardsTaken> Discards(ModelProgram program) =>
        Takes(program, command => command.Command.DeclaringType == "CardCmd" && command.Command.Name is "Discard" or "DiscardAndDraw" ? 1 : null, collections: true);

    /// <summary>The play's transforms (CardCmd.Transform, TransformTo, TransformToRandom: the card or the list is their first argument), in order.</summary>
    public static IReadOnlyList<CardsTaken> Transforms(ModelProgram program) =>
        Takes(program, command => command.Command.DeclaringType == "CardCmd" && command.Command.Name.StartsWith("Transform", StringComparison.Ordinal) ? 0 : null, collections: true);

    /// <summary>The play's moves into your hand (CardPileCmd.Add to the hand, after no pick from another player's cards), in order.</summary>
    public static IReadOnlyList<CardsTaken> TakesIntoHandOf(ModelProgram program) =>
        Takes(program, command => Is(command, "CardPileCmd", "Add") && Argument(command, 1) == HandPile ? 0 : null, collections: true)
            .Where(take => take.Pick == null || PicksYourCards(take.Pick)).ToList();

    /// <summary>
    /// Each command <paramref name="cardsArgument"/> names (with the argument holding the cards it acts on), by how it takes them: a pick's
    /// card or cards (Picked), a roll's (Random), each card of a collection a loop goes over (Each; a loop over a pick's result is
    /// Picked), and with <paramref name="collections"/> a collection given whole (your hand, a Where of it: Each), the cards a draw gave
    /// (Drawn) or the ones looked at on top of your draw pile (LookedAt). Anything else (the card itself, a card it can't tell) isn't one.
    /// </summary>
    private static IReadOnlyList<CardsTaken> Takes(ModelProgram program, Func<CommandStep, int?> cardsArgument, bool collections) =>
        Find(program, command => cardsArgument(command) is { } index && index < command.Values.Count)
            .Select(found => Taken(found.Command, Unstored(found.Command.Values[cardsArgument(found.Command)!.Value])!, found.Condition, found.Loops.Count > 0 ? found.Loops[^1] : null,
                found.Pick, found.LoopBefore, collections))
            .OfType<CardsTaken>().ToList();

    private static CardsTaken? Taken(CommandStep command, ProgramValue cards, ProgramCondition condition, LoopStep? loop, ChoiceStep? lastPick, LoopStep? lastLoop, bool collections)
    {
        static bool IsPick(ProgramValue? value) => value != null && value.Parts().Any(part => part is ResultValue { Command: var result } && result.StartsWith("CardSelectCmd.", StringComparison.Ordinal));
        if (cards is NamedValue { Name: "each" } && loop != null)
        {
            if (IsPick(Unwrapped(loop.Items)) && Unwrapped(loop.Items) is ResultValue) return new CardsTaken(command, condition, TakeWay.Picked, loop.Items, lastPick);
            return loop.Items != null ? new CardsTaken(command, condition, TakeWay.Each, loop.Items, Pick: null) : null;
        }
        if (IsPick(cards) && !(collections && cards.Parts().Any(part => part is QueryValue { Name: "Take" }))) return new CardsTaken(command, condition, TakeWay.Picked, Items: null, lastPick);
        if (cards is QueryValue { Name: "NextItem", Arguments: [var from] }) return new CardsTaken(command, condition, TakeWay.Random, from, Pick: null);
        if (!collections) return null;
        if (Unwrapped(cards) is QueryValue { Name: "TakeRandom", Arguments: [var rolledFrom, var count, ..] })
            return new CardsTaken(command, condition, TakeWay.Random, rolledFrom, Pick: null) { RollCount = count };
        if (cards.Parts().Any(part => part is ResultValue { Command: "CardPileCmd.Draw" })) return new CardsTaken(command, condition, TakeWay.Drawn, cards, Pick: null);
        if (cards.Parts().Any(part => part is QueryValue { Name: "Take", Arguments.Count: 2 })) return new CardsTaken(command, condition, TakeWay.LookedAt, cards, lastPick);
        // A list the play fills in a loop and then gives whole (Compact's transformations): that loop's cards.
        if (Unwrapped(cards) is QueryValue { Name: ".ctor", Type: var listType } && listType.StartsWith("List", StringComparison.Ordinal) && lastLoop?.Items != null)
            return new CardsTaken(command, condition, TakeWay.Each, lastLoop.Items, Pick: null);
        // The first card of a collection (Punch of Joy's Sadness): one at most.
        if (Unwrapped(cards) is QueryValue { Name: "FirstOrDefault" or "First" or "Item" or "get_Item" } single && (single.Of ?? (single.Arguments.Count > 0 ? single.Arguments[0] : null)) is { } collection)
            return new CardsTaken(command, condition, TakeWay.Each, collection, Pick: null) { Single = true };
        return IsCollection(cards) ? new CardsTaken(command, condition, TakeWay.Each, cards, Pick: null) : null;
    }

    /// <summary>A value that holds cards (a pile's Cards, a Where, a ToList of them), not one card.</summary>
    private static bool IsCollection(ProgramValue value) => Unwrapped(value) is QueryValue query
        && (query.Name is "Cards" or "Where" or "OfType" or "ToList" or "Except" || query.ValueType.Contains("List", StringComparison.Ordinal)
            || query.ValueType.Contains("Enumerable", StringComparison.Ordinal));

    /// <summary>A value as stored (a local holding it is it).</summary>
    private static ProgramValue? Unstored(ProgramValue? value) => value is StoredValue stored ? Unstored(stored.Value) : value;

    /// <summary>It exhausts other cards in its play, read from its program (True Grit, Fiend Fire, Second Wind, Cleanse...).</summary>
    public static bool ExhaustsOthers(ModelProgram program) => ExhaustsOfOthers(program).Count > 0;

    // ---- cards moved between piles (CardPileCmd.Add)

    /// <summary>What a CardPileCmd.Add in a card's play does with the cards it moves.</summary>
    private enum Move { IntoYourHand, PutBack, Elsewhere, Unpicked }

    /// <summary>The pile an Add names (its second argument: PileType as a number).</summary>
    private static readonly string HandPile = ((int)MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand).ToString();
    private static readonly string DrawPile = ((int)MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw).ToString();

    /// <summary>
    /// Each CardPileCmd.Add of the play, by what it does: into your hand (its pile is the hand, and no pick before it is from another
    /// player's cards: Hologram, Wish, Summon Forth); back onto your draw pile after a pick from your hand (Glimmer); after a pick,
    /// anywhere else (Headbutt onto your draw pile, Tutor into a teammate's hand); or with no pick before it (Reboot's whole hand).
    /// </summary>
    private static IEnumerable<Move> Moves(ModelProgram program)
    {
        ChoiceStep? pick = null;
        foreach (var step in program.All())
        {
            if (step is ChoiceStep choice) pick = choice;
            if (step is not CommandStep command || !Is(command, "CardPileCmd", "Add")) continue;
            var pile = Argument(command, 1);
            if (pile == HandPile && (pick == null || PicksYourCards(pick))) yield return Move.IntoYourHand;
            else if (pick == null) yield return Move.Unpicked;
            else if (pile == DrawPile && pick.Command.Name == "FromHand") yield return Move.PutBack;
            else yield return Move.Elsewhere;
        }
    }

    /// <summary>
    /// The pick is from your cards: it names you (Owner) among its arguments, or its arguments can't be read (Arrow Stab's). One whose
    /// arguments are all read and never name you picks another player's (Tutor: its target's draw pile).
    /// </summary>
    private static bool PicksYourCards(ChoiceStep pick) => pick.Arguments.Contains("Owner") || pick.Arguments.Contains("?");

    /// <summary>Moves cards from your piles into your hand (CardPileCmd.Add to the hand: Hologram, Wish, Dredge, Summon Forth...).</summary>
    public static bool TakesIntoHand(ModelProgram program) => Moves(program).Contains(Move.IntoYourHand);

    /// <summary>Puts cards you pick from your hand back on your draw pile (Glimmer, Photon Cut, Thinking Ahead).</summary>
    public static bool PutsBackFromHand(ModelProgram program) => Moves(program).Contains(Move.PutBack);

    /// <summary>
    /// Moves a card it picks somewhere your hand doesn't see: from your discard pile onto your draw pile (Headbutt, Recall, Cosmic
    /// Indifference), or into a teammate's hand (Tutor).
    /// </summary>
    public static bool MovesCardsElsewhere(ModelProgram program) =>
        Moves(program).ToList() is var moves && moves.Contains(Move.Elsewhere) && !moves.Contains(Move.IntoYourHand) && !moves.Contains(Move.PutBack);

    public static bool HitsXTimes(Type cardType) => HitsXTimes(CardPrograms.Of(cardType));
    public static bool AttacksOnceRepeatingSomethingElse(Type cardType) => AttacksOnceRepeatingSomethingElse(CardPrograms.Of(cardType));

    // ---- attacks

    /// <summary>An attack whose hit count is the card's X (WithHitCount(ResolveEnergyXValue()): Whirlwind, Skewer, Volley).</summary>
    public static bool HitsXTimes(ModelProgram program) =>
        program.Commands.Any(command => Is(command, "DamageCmd", "Attack") && command.HitCount == "ResolveEnergyXValue");

    /// <summary>
    /// Attacks once (no hit count) and repeats something else its Repeat times (Ice Lance channels Repeat Frost, Heirloom Hammer makes
    /// Repeat copies): its Repeat value isn't its hits.
    /// </summary>
    public static bool AttacksOnceRepeatingSomethingElse(ModelProgram program) =>
        program.Commands.Any(command => Is(command, "DamageCmd", "Attack") && command.HitCount == null)
        && program.All().OfType<LoopStep>().Any(loop => loop.Over == "Repeat" && !new ModelProgram("", loop.Body).Commands.Any(command => Is(command, "DamageCmd", "Attack")));

    public static bool AutoPlaysFromDrawPile(Type cardType) => AutoPlaysFromDrawPile(CardPrograms.Of(cardType));
    public static bool SummonsEachPlayer(Type cardType) => SummonsEachPlayer(CardPrograms.Of(cardType));
    public static bool DislodgesArrows(Type cardType) => DislodgesArrows(CardPrograms.Of(cardType));
    public static bool KeepsStretchForReplays(Type cardType) => KeepsStretchForReplays(CardPrograms.Of(cardType));

    public static bool TakesWholeHand(Type cardType) => TakesWholeHand(CardPrograms.Of(cardType));

    /// <summary>
    /// Takes every card in your hand, whatever it is: a loop over your hand's cards that exhausts, discards or moves each (Fiend Fire,
    /// The Devil, Reboot), or a discard of the hand's cards (Storm of Steel, Calculated Gamble). Not a loop over some of them (Second
    /// Wind's non-Attacks, Growth's Sadness: a filter, Where).
    /// </summary>
    public static bool TakesWholeHand(ModelProgram program) =>
        program.All().OfType<LoopStep>().Any(loop => IsWholeHand(loop.Over.Replace("each of ", ""))
                                                     && loop.Body.OfType<CommandStep>().Any(command => TakesFromHand(command)))
        || program.Commands.Any(command => command.Command.DeclaringType == "CardCmd" && command.Command.Name is "Discard" or "DiscardAndDraw"
                                           && IsWholeHand(Argument(command, 1)));

    private static bool IsWholeHand(string? cards) => cards is "Hand.Cards" or "Hand.Cards.ToList";

    // ---- counts a play reads as it runs (decision #189)

    /// <summary>The collection is every card in your hand (PileType.Hand.GetPile(owner).Cards, a list of them).</summary>
    public static bool IsYourWholeHand(ProgramValue? items) =>
        Unwrapped(items) is QueryValue { Name: "Cards", Of: QueryValue { Name: "GetPile", Arguments: [ConstantValue { Value: var pile }, ..] } }
        && Convert.ToInt32(pile) == (int)MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand;

    /// <summary>
    /// The value counts the cards of a take's collection (Fiend Fire's hits: its hand's cards before it exhausts them; Calculated Gamble's
    /// draws; The Devil's auto-plays): what that take takes, read as it runs (SimCount.CardsTaken).
    /// </summary>
    public static bool CountsTaken(ProgramValue? value, CardsTaken take)
    {
        if (value == null || take.Items == null) return false;
        var items = Unwrapped(take.Items)?.ToString();
        foreach (var part in value.Parts())
            if (part is QueryValue { Name: "Count" } count && Unwrapped(count.Of ?? (count.Arguments.Count > 0 ? count.Arguments[0] : null))?.ToString() is { } counted
                && counted == items)
                return true;
        return false;
    }

    /// <summary>The value reads the room in your hand (CardPile.MaxCardsInHand less your hand: Scrawl, The World, Anointed): SimCount.HandRoom.</summary>
    public static bool ReadsHandRoom(ProgramValue? value) =>
        value != null && value.Parts().Any(part => part is QueryValue { Name: "MaxCardsInHand" } or NamedValue { Name: "MaxCardsInHand" });

    /// <summary>
    /// A count capped by your hand's room, without the room (The World's Min(Cards, room) is Cards): what it would be with room to spare.
    /// Null when it's the room alone (Anointed, Scrawl).
    /// </summary>
    public static ProgramValue? WithoutRoom(ProgramValue value)
    {
        value = Unstored(value)!;
        if (!ReadsHandRoom(value)) return value;
        return value switch
        {
            QueryValue { Name: "Max", Arguments: [ConstantValue, var inner] } => WithoutRoom(inner),
            QueryValue { Name: "Min", Arguments: [var left, var right] } => ReadsHandRoom(left) ? WithoutRoom(right) : WithoutRoom(left),
            _ => null,
        };
    }

    private static bool TakesFromHand(CommandStep command) =>
        command.Command.DeclaringType == "CardCmd" && command.Command.Name is "Exhaust" or "Discard" || Is(command, "CardPileCmd", "Add");

    // ---- the rest

    /// <summary>Plays cards from the top of your draw pile (CardPileCmd.AutoPlayFromDrawPile: Havoc, Cascade, The Devil).</summary>
    public static bool AutoPlaysFromDrawPile(ModelProgram program) => program.Commands.Any(command => Is(command, "CardPileCmd", "AutoPlayFromDrawPile"));

    /// <summary>Summons each player's Osty, a loop over the players (Legion of Bone: OstyCmd.Summon for each, not only you).</summary>
    public static bool SummonsEachPlayer(ModelProgram program) =>
        program.All().OfType<LoopStep>().Any(loop => loop.Body.OfType<CommandStep>().Any(command => Is(command, "OstyCmd", "Summon") && Argument(command, 1) is { } player && player != "Owner"));

    /// <summary>Knocks your arrows out of its target (ArrowCmd.DislodgeArrow on the target: Recall, Uproot, Wrest).</summary>
    public static bool DislodgesArrows(ModelProgram program) => program.Commands.Any(command => Is(command, "ArrowCmd", "DislodgeArrow") && Argument(command, 0) == Target);

    /// <summary>
    /// Gains block, then gives you Stretch (Cho Ki): the Stretch is for a replay's later plays, so this play's block doesn't spend it.
    /// </summary>
    public static bool KeepsStretchForReplays(ModelProgram program)
    {
        var blocked = false;
        foreach (var command in program.Commands)
        {
            if (Is(command, "CreatureCmd", "GainBlock") && Argument(command, 0) == You) blocked = true;
            else if (blocked && command.Command.DeclaringType == "PowerCmd" && command.Command.Name == "Apply"
                     && command.Command.GenericArguments.SequenceEqual(new[] { nameof(MegaCrit.Sts2.Core.Models.Powers.StretchPower) }) && Argument(command, 1) == You)
                return true;
        }
        return false;
    }

    public static bool MakesSadness(Type cardType) => MakesSadness(CardPrograms.Of(cardType));
    public static bool Forges(Type cardType) => Forges(CardPrograms.Of(cardType));

    // ---- made cards

    /// <summary>
    /// Makes Sadness (CombatState.CreateCard&lt;Sadness&gt; in its play: Jest's into your hand, Mourning Blow turning into one): the
    /// statuses it makes are Sadness, which Fertilizer and All Is Well react to.
    /// </summary>
    public static bool MakesSadness(ModelProgram program) => program.All().OfType<CreateStep>().Any(create => create.Card == nameof(MegaCrit.Sts2.Core.Models.Cards.Sadness));

    /// <summary>Forges (ForgeCmd.Forge: a Sovereign Blade made the first time, more damage after).</summary>
    public static bool Forges(ModelProgram program) => program.Commands.Any(command => Is(command, "ForgeCmd", "Forge"));

    // ---- commands found with what they run on

    /// <summary>A command of the play, with everything it runs on (its own condition and every loop's around it) and the loops it's in.</summary>
    public sealed record Found(CommandStep Command, ProgramCondition Condition, IReadOnlyList<LoopStep> Loops)
    {
        /// <summary>The last pick before it in the play (CardSelectCmd), whose card or cards it may act on.</summary>
        public ChoiceStep? Pick { get; init; }

        /// <summary>The last loop the play finished before it (a list a loop filled, given whole after it: Compact).</summary>
        public LoopStep? LoopBefore { get; init; }
    }

    /// <summary>
    /// The play's commands <paramref name="which"/> picks, in order, each with its condition, its loops, the pick before it and the loop
    /// finished before it: the one walk every effect kind is a filter of (draws, takes, attacks, block, orbs...).
    /// </summary>
    public static IReadOnlyList<Found> Find(ModelProgram program, Func<CommandStep, bool> which)
    {
        var found = new List<Found>();
        ChoiceStep? lastPick = null;
        LoopStep? lastLoop = null;
        void Walk(IEnumerable<ProgramStep> steps, ProgramCondition outer, IReadOnlyList<LoopStep> loops)
        {
            foreach (var step in steps)
            {
                var condition = step.Condition is { } own ? outer.And(own) : outer;
                switch (step)
                {
                    case ChoiceStep choice:
                        lastPick = choice;
                        break;
                    case LoopStep loop:
                        Walk(loop.Body, condition, loops.Append(loop).ToList());
                        lastLoop = loop;
                        break;
                    case CommandStep command:
                        if (which(command)) found.Add(new Found(command, condition, loops) { Pick = lastPick, LoopBefore = lastLoop });
                        Walk(command.Inner, condition, loops);
                        break;
                }
            }
        }
        Walk(program.Steps, ProgramCondition.Always, Array.Empty<LoopStep>());
        return found;
    }

    public static bool TriggersPoison(Type cardType) => TriggersPoison(CardPrograms.Of(cardType));

    /// <summary>Sets off poison during the play (PoisonPower.Trigger, a power's action the card runs: Outbreak on every enemy's poison).</summary>
    public static bool TriggersPoison(ModelProgram program) =>
        program.Commands.Any(command => command.Command is { DeclaringType: nameof(MegaCrit.Sts2.Core.Models.Powers.PoisonPower), Name: "Trigger" });

    // ---- attacks

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<Found>> AttackSteps = new();

    /// <summary>The play's attacks (DamageCmd.Attack, built up to Execute), in order, each with its condition and loops.</summary>
    public static IReadOnlyList<Found> Attacks(Type cardType) => AttackSteps.GetOrAdd(cardType, type => Attacks(CardPrograms.Of(type)));
    public static IReadOnlyList<Found> Attacks(ModelProgram program) => Find(program, command => Is(command, "DamageCmd", "Attack"));

    // ---- block

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<Found>> BlockSteps = new();

    /// <summary>The play's block gains (CreatureCmd.GainBlock: for you, the teammate it's aimed at, or each player a loop goes over), in order.</summary>
    public static IReadOnlyList<Found> BlockGains(Type cardType) => BlockSteps.GetOrAdd(cardType, type => BlockGains(CardPrograms.Of(type)));
    public static IReadOnlyList<Found> BlockGains(ModelProgram program) => Find(program, command => Is(command, "CreatureCmd", "GainBlock"));

    /// <summary>The gain is for each item of the loop it's in (Rally: each living player), not the same creature each round.</summary>
    public static bool GainsForEachOfLoop(CommandStep gain) => gain.Values.Count > 0 && Unstored(gain.Values[0]) is NamedValue { Name: "each" };

    /// <summary>
    /// What a block gain's amount (CreatureCmd.GainBlock's second argument) reads as it runs: the results of the play's attack summed
    /// (Fisticuffs: TotalDamage + OverkillDamage of AttackCommand.Execute's results), the gainer's own block (Entrench), a calculated block whose
    /// multiplier is its target's block (Mimic: CalculatedBlockVar.WithMultiplier((card, target) =&gt; target?.Block)); else an amount given whole.
    /// </summary>
    public static SpireQoL.Sim.SimBlockBase BlockBaseOf(CommandStep gain, Type cardType)
    {
        if (gain.Values.Count < 2 || Unstored(gain.Values[1]) is not { } amount) return SpireQoL.Sim.SimBlockBase.Given;
        if (ReadsAttackResults(amount)) return SpireQoL.Sim.SimBlockBase.AttackDealt;
        if (amount is QueryValue { Name: "Block", Of: { } whose } && Unstored(gain.Values[0])?.ToString() == Unstored(whose)?.ToString()) return SpireQoL.Sim.SimBlockBase.OwnBlock;
        if (amount is CardValue { Key: var key } && ProgramReader.CalculatedMultipliers(cardType, RuntimeCode.Read).TryGetValue(key, out var multiplier)
            && multiplier.Parts().Any(part => part is QueryValue { Name: "Block", Of: { } of } && of.Parts().Any(inner => inner is ParameterValue { Name: "target" } or NamedValue { Name: "target" })))
            return SpireQoL.Sim.SimBlockBase.TargetBlock;
        return SpireQoL.Sim.SimBlockBase.Given;
    }

    /// <summary>The value sums what an attack of the play dealt (its results' TotalDamage and OverkillDamage: Fisticuffs' block, Blossom Shot's Stretch).</summary>
    public static bool ReadsAttackResults(ProgramValue value) =>
        value.Parts().Any(part => part is ResultValue { Command: var command } && command.StartsWith("AttackCommand.", StringComparison.Ordinal)
                                  || part is QueryValue { Name: "Results" })
        && value.Parts().Any(part => part is QueryValue { Name: "TotalDamage" or "OverkillDamage" });

    /// <summary>
    /// The play deals a hit's damage again to the target's other side (Rain Shot, Omnislice: CreatureCmd.Damage of the first damage's
    /// TotalDamage + OverkillDamage to every other hittable creature on the receiver's side, its caps only): the attack's splash.
    /// </summary>
    public static bool SplashesDamage(Type cardType) => SplashesDamage(CardPrograms.Of(cardType));

    public static bool SplashesDamage(ModelProgram program) => program.Commands.Any(SplashOf);

    /// <summary>A CreatureCmd.Damage of what an earlier CreatureCmd.Damage dealt, to the others of its receiver's side (a splash).</summary>
    public static bool SplashOf(CommandStep command) =>
        Is(command, "CreatureCmd", "Damage") && command.Values.Count > 2
        && command.Values[2].Parts().Any(part => part is ResultValue { Command: "CreatureCmd.Damage" }) && command.Values[2].Parts().Any(part => part is QueryValue { Name: "TotalDamage" })
        && command.Values[1].Parts().Any(part => part is QueryValue { Name: "Except" });

    /// <summary>
    /// The play's damage to every enemy goes round again for each kill (Echoing Slash: a loop while a count stays above 0 around a
    /// CreatureCmd.Damage of every enemy, its code counting its results' kills, DamageResult.WasTargetKilled): AttackAction.RepeatPerKill.
    /// </summary>
    public static bool RepeatsPerKill(Type cardType) =>
        CardCode.Calls(cardType, "DamageResult", "get_WasTargetKilled")
        && CardPrograms.Of(cardType).All().OfType<LoopStep>().Any(loop =>
            loop.While is { Terms: [[{ Holds: true, Test: CompareValue { Op: Comparison.Greater, Right: ConstantValue { Value: 0 } } }]] }
            && new ModelProgram("", loop.Body).Commands.Any(command => Is(command, "CreatureCmd", "Damage") && command.Values.Count > 1
                                                                      && command.Values[1].Parts().Any(part => part is QueryValue { Name: "HittableEnemies" })));

    /// <summary>
    /// Every command of the play reading a hit's overkill (DamageResult.OverkillDamage) is one the simulator follows: a splash (SplashOf), a
    /// block gain of what the attack dealt (BlockBaseOf: Fisticuffs) or Stretch from it (Blossom Shot); the card scan flags any other.
    /// </summary>
    public static bool OverkillReadsFollowed(Type cardType)
    {
        var readers = CardPrograms.Of(cardType).Commands.Where(command => command.Values.Any(value => value.Parts().Any(part => part is QueryValue { Name: "OverkillDamage" }))).ToList();
        return readers.Count > 0 && readers.All(command => SplashOf(command)
                                                           || Is(command, "CreatureCmd", "GainBlock") && BlockBaseOf(command, cardType) == SpireQoL.Sim.SimBlockBase.AttackDealt
                                                           || command.Command is { DeclaringType: "PowerCmd", Name: "Apply", GenericArguments: [nameof(MegaCrit.Sts2.Core.Models.Powers.StretchPower)] }
                                                              && command.Values.Count > 2 && ReadsAttackResults(command.Values[2]));
    }

    /// <summary>
    /// How many rounds a loop goes can be read from its code: a foreach over a collection read whole, or a counter below a bound read
    /// whole (i &lt; Repeat, i &lt; blockGains). The card scan holds a loop doing something the predictions count to it.
    /// </summary>
    public static bool RoundsReadable(LoopStep loop) =>
        loop.Items != null && !loop.Items.Parts().Any(part => part is UnknownValue)
        || loop.While is { Terms: [[{ Holds: true, Test: CompareValue { Op: Comparison.Less or Comparison.LessOrEqual } bound }]] } && !bound.Right.Parts().Any(part => part is UnknownValue);

    // ---- orbs

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<Found>> ChannelSteps = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<Found>> EvokeSteps = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<Found>> SlotSteps = new();

    /// <summary>The play's channels (OrbCmd.Channel&lt;T&gt;, or Channel of a random orb: Chaos, The Magician), in order.</summary>
    public static IReadOnlyList<Found> OrbChannels(Type cardType) => ChannelSteps.GetOrAdd(cardType, type => OrbChannels(CardPrograms.Of(type)));
    public static IReadOnlyList<Found> OrbChannels(ModelProgram program) => Find(program, command => Is(command, "OrbCmd", "Channel"));

    /// <summary>The play's evokes of your front orb (OrbCmd.EvokeNext: Dualcast, Quadcast, Multicast, Shatter), in order.</summary>
    public static IReadOnlyList<Found> OrbEvokes(Type cardType) => EvokeSteps.GetOrAdd(cardType, type => OrbEvokes(CardPrograms.Of(type)));
    public static IReadOnlyList<Found> OrbEvokes(ModelProgram program) => Find(program, command => Is(command, "OrbCmd", "EvokeNext"));

    /// <summary>The play's orb slots added or removed (OrbCmd.AddSlots, RemoveSlots: their count is the second argument).</summary>
    public static IReadOnlyList<Found> OrbSlotChanges(Type cardType) => SlotSteps.GetOrAdd(cardType, type => OrbSlotChanges(CardPrograms.Of(type)));
    public static IReadOnlyList<Found> OrbSlotChanges(ModelProgram program) =>
        Find(program, command => command.Command.DeclaringType == "OrbCmd" && command.Command.Name is "AddSlots" or "RemoveSlots");

    /// <summary>
    /// The orb a channel makes: its class's kind (Channel&lt;FrostOrb&gt;: Frost), Random for a random orb (a Channel given
    /// OrbModel.GetRandomOrb's orb), or null for an orb the simulator doesn't know.
    /// </summary>
    public static SpireQoL.Sim.OrbKind? OrbOf(CommandStep channel)
    {
        if (channel.Command.GenericArguments is [var orb])
            return orb.EndsWith("Orb", StringComparison.Ordinal) && Enum.TryParse<SpireQoL.Sim.OrbKind>(orb[..^3], out var kind) && kind is not (SpireQoL.Sim.OrbKind.Unknown or SpireQoL.Sim.OrbKind.Random)
                ? kind : null;
        return channel.Values.Any(value => value.Parts().Any(part => part is QueryValue { Name: "GetRandomOrb" })) ? SpireQoL.Sim.OrbKind.Random : null;
    }

    /// <summary>The play channels or evokes orbs (OrbCmd.Channel, EvokeNext): its orbs' evokes are its simulation's.</summary>
    public static bool TouchesOrbs(Type cardType) => OrbChannels(cardType).Count > 0 || OrbEvokes(cardType).Count > 0;

    /// <summary>The simulator channels every orb the card does (each channel's orb is a kind it knows, or a random one).</summary>
    public static bool ChannelsKnownOrbs(Type cardType) => OrbChannels(cardType) is { Count: > 0 } channels && channels.All(channel => OrbOf(channel.Command) != null);

    /// <summary>
    /// The evokes go over every orb you have (a loop whose rounds are your orbs' count: Shatter), each orb evoked that many times; 0 when
    /// they evoke the front orb (Dualcast, Quadcast, Multicast).
    /// </summary>
    public static int EvokesPerOrbOfAll(Type cardType)
    {
        var evokes = OrbEvokes(cardType);
        var overAll = evokes.Where(evoke => evoke.Loops.Any(loop => loop.While?.Literals.Any(literal => literal.Test.Parts().Any(part => part is QueryValue { Name: "Orbs" })) == true)).ToList();
        return overAll.Count;
    }

    public static bool DiscardsOnlyPicks(Type cardType) => DiscardsOnlyPicks(CardPrograms.Of(cardType));

    /// <summary>
    /// Every card it discards is one you pick: each CardCmd.Discard (or DiscardAndDraw) follows a choice (CardSelectCmd) in its code
    /// (Acrobatics, Prepared, The Hermit), not the whole hand (Storm of Steel, Calculated Gamble). A pick is a card, never a particular
    /// one (decision #92): what only a particular card would do then (a Sly card playing itself) isn't followed.
    /// </summary>
    public static bool DiscardsOnlyPicks(ModelProgram program)
    {
        var picked = false;
        var discards = false;
        foreach (var step in program.All())
        {
            if (step is ChoiceStep) picked = true;
            if (step is CommandStep command && command.Command.DeclaringType == "CardCmd" && command.Command.Name is "Discard" or "DiscardAndDraw")
            {
                if (!picked) return false;
                discards = true;
            }
        }
        return discards;
    }

    private static bool Is(CommandStep command, string type, string name) => command.Command.DeclaringType == type && command.Command.Name == name;

    private static string? Argument(CommandStep command, int index) => index < command.Arguments.Count ? command.Arguments[index] : null;

    /// <summary>The commands not inside a loop.</summary>
    private static IEnumerable<CommandStep> Outside(IEnumerable<ProgramStep> steps)
    {
        foreach (var step in steps)
        {
            if (step is not CommandStep command) continue;
            yield return command;
            foreach (var inner in Outside(command.Inner)) yield return inner;
        }
    }
}

using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Procs.Programs;

/// <summary>
/// The cards a play makes, read from its program (decision #194): each step that adds made cards to a pile
/// (CardPileCmd.AddGeneratedCardToCombat, AddGeneratedCardsToCombat, AddToCombatAndPreview), a forge (ForgeCmd.Forge: the Sovereign Blade),
/// and a card the played card turns into after its play (CardModel.TransformIntoAfterPlay: Mourning Blow). Each make is read from its
/// own arguments: how many one call makes, the card it makes when the code names it, whose pile it goes to, who makes it (whose
/// created-card hooks hear it: Hook.AfterCardGeneratedForCombat's creator) and the pile. No card is named.
/// </summary>
public static class MadeCards
{
    /// <summary>What a made card is to the created-card hooks: a status, a Sadness (Fertilizer, All Is Well), or any other card.</summary>
    public enum Kind { Card, Status, Sadness }

    /// <summary>How a make step makes its cards.</summary>
    public enum Way
    {
        /// <summary>A command adding the cards to a pile.</summary>
        Added,
        /// <summary>A forge: a Sovereign Blade into your hand when you have none (ForgeCmd.Forge).</summary>
        Forged,
        /// <summary>The played card turns into it once its last play is over (CardModel.TransformIntoAfterPlay, CardCmd.Transform).</summary>
        AfterPlay,
    }

    /// <summary>One make of the play.</summary>
    /// <param name="Found">The step, its condition and the loops it's in (for a card it turns into: none).</param>
    /// <param name="PerCall">How many one call makes (a value on the board); null: one, or the rounds of <paramref name="FilledBy"/>.</param>
    /// <param name="FilledBy">The loop that filled the list the call adds (Crash Landing's Debris, Shiv.CreateInHand's Shivs).</param>
    /// <param name="Card">The made card's class, when the code names it (CreateCard&lt;Dazed&gt;, a clone of the card, Soul.Create); null: a roll or a pick.</param>
    /// <param name="For">Whose pile it goes to (a player value of the code); null: the card's owner.</param>
    /// <param name="Creator">Who makes it (the command's creator argument); null: whose pile it goes to.</param>
    /// <param name="Pile">The pile it goes to; null when the code doesn't write it as a constant there.</param>
    public sealed record Make(CardEffects.Found? Found, Way By, ProgramValue? PerCall, LoopStep? FilledBy, string? Card, ProgramValue? For, ProgramValue? Creator, PileType? Pile)
    {
        /// <summary>The value its count reads (its own count, or the bound of the loop that filled its list): your hand's room, the cards a take took.</summary>
        public ProgramValue? CountValue => PerCall ?? (FilledBy?.While is { Terms: [[{ Test: CompareValue { Op: Comparison.Less or Comparison.LessOrEqual } bound }]] } ? bound.Right : null);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<Make>> Cache = new();

    /// <summary>The makes of the card's play, in its code's order.</summary>
    public static IReadOnlyList<Make> Of(Type cardType) => Cache.GetOrAdd(cardType, type => Of(CardPrograms.Of(type), type));

    private static readonly HashSet<string> AddCommands = new() { "AddGeneratedCardToCombat", "AddGeneratedCardsToCombat", "AddToCombatAndPreview" };

    /// <summary>Is the command one that makes cards (a make's step)?</summary>
    public static bool Makes(CommandStep command) =>
        command.Command.DeclaringType == "CardPileCmd" && AddCommands.Contains(command.Command.Name) || command.Command is { DeclaringType: "ForgeCmd", Name: "Forge" };

    public static IReadOnlyList<Make> Of(ModelProgram program, Type cardType)
    {
        var makes = new List<Make>();
        var named = new HashSet<string>();
        foreach (var found in CardEffects.Find(program, Makes))
        {
            var make = Read(found, cardType);
            makes.Add(make);
            if (make.Card != null) named.Add(make.Card);
        }
        // A card the played card turns into after its play (it sets TransformIntoAfterPlay to a card it makes, which no command adds).
        if (CardCode.Calls(cardType, "CardModel", "set_TransformIntoAfterPlay"))
            foreach (var create in program.All().OfType<CreateStep>().Where(create => !named.Contains(create.Card)).DistinctBy(create => create.Card))
                makes.Add(new Make(Found: null, Way.AfterPlay, PerCall: null, FilledBy: null, create.Card, For: null, Creator: null, Pile: null));
        return makes;
    }

    private static Make Read(CardEffects.Found found, Type cardType)
    {
        var command = found.Command;
        ProgramValue? Value(int index) => index < command.Values.Count ? Unstored(command.Values[index]) : null;
        if (command.Command.DeclaringType == "ForgeCmd")
            return new Make(found, Way.Forged, PerCall: null, FilledBy: null, Card: nameof(MegaCrit.Sts2.Core.Models.Cards.SovereignBlade), For: null, Creator: null, PileType.Hand);
        if (command.Command.Name == "AddToCombatAndPreview")
        {
            // AddToCombatAndPreview<T>(target, pile, count, creator): count of T made for the target's player, each through AddGeneratedCardToCombat.
            var target = Value(0);
            var previewed = command.Command.GenericArguments.Count > 0 ? command.Command.GenericArguments[0] : null;
            return new Make(found, Way.Added, Value(2), FilledBy: null, previewed, target is QueryValue { Name: "Creature" } creature ? creature.Of : target, Value(3), PileOf(Value(1)));
        }
        var made = Value(0);
        var single = command.Command.Name == "AddGeneratedCardToCombat";
        // What the made card is, and whose: a card the code makes (CreateCard<T>(player), a clone, Soul.Create(player, count)), a card factory's
        // cards for a player, or a loop's item over one of those.
        var source = made is NamedValue { Name: "each" } && found.Loops.Count > 0 && found.Loops[^1].Items is { } items ? Unstored(items) : made;
        var maker = source?.Parts().OfType<QueryValue>().FirstOrDefault(value => value.Name is "CreateCard" or "CreateClone" or "CreateCloneForPlayer" or "Create" or "GetForCombat" or "GetDistinctForCombat");
        string? card = maker switch
        {
            { Name: "CreateCard", GenericArguments: [var type] } => type,
            { Name: "CreateClone" or "CreateCloneForPlayer", Of: NamedValue { Name: "this" } } => cardType.Name,
            { Name: "Create", Type: var owner } when owner != "CombatState" => owner,
            _ => null,
        };
        ProgramValue? whose = maker switch
        {
            { Name: "CreateCloneForPlayer", Arguments: [var player, ..] } => player,
            { Name: "CreateCard" or "Create" or "GetForCombat" or "GetDistinctForCombat", Arguments: [var player, ..] } => player,
            _ => null,
        };
        ProgramValue? perCall = null;
        LoopStep? filledBy = null;
        if (!single)
        {
            perCall = maker is { Name: "Create", Arguments.Count: >= 2 } create && source == made ? create.Arguments[1] : ProgramEvaluator.FactoryCount(made);
            // A list a loop filled with the cards it made (Crash Landing's Debris, Shiv.CreateInHand's Shivs): that loop's rounds, its card the one it made.
            if (perCall == null && found.LoopBefore is { } loop && new ModelProgram("", loop.Body).All().OfType<CreateStep>().ToList() is { Count: > 0 } creates
                && creates.Select(create => create.Card).Distinct().ToList() is [var filled])
            {
                filledBy = loop;
                card ??= filled;
                // Whose they are: the player each was made for (CreateCard<T>(owner): Self-Deprecate's Joy for each teammate, Shiv.CreateInHand's owner).
                if (maker == null && creates[0].Values is [var owner, ..]) whose = Unstored(owner);
            }
        }
        return new Make(found, Way.Added, perCall, filledBy, card, whose, Value(2), PileOf(Value(1)));
    }


    /// <summary>What a card's makes give on the board before its play, its own (the ones it makes as their creator): for the card scan and your hand after.</summary>
    /// <param name="Made">Every card it makes.</param>
    /// <param name="IntoYourHand">Of those, the ones into your hand.</param>
    /// <param name="Unread">A make whose count can't be read on the board.</param>
    public sealed record OnBoard(int Made, int Statuses, int Sadness, int IntoYourHand, bool Forged, bool Unread);

    /// <summary>
    /// The card's makes on the board, as one play of it now would make them: each make's count (CountOn), times the players of your side it
    /// makes them for when you're their creator (Glimpse Beyond's Souls for each; Blade Symphony's Shivs are each player's own); a forge's
    /// blade when you have none outside your exhaust pile (ForgeCmd.Forge); a card it turns into, one.
    /// </summary>
    public static OnBoard Summary(CardModel card)
    {
        var type = card.GetType();
        var program = CardPrograms.Of(type);
        var board = new ProgramBoard(card);
        int made = 0, statuses = 0, sadness = 0, intoHand = 0;
        bool forged = false, unread = false;
        foreach (var make in Of(type))
        {
            int count;
            if (make.By == Way.Forged)
            {
                forged = !HasBlade(card);
                count = forged ? 1 : 0;
            }
            else if (make.By == Way.AfterPlay) count = 1;
            else
            {
                // A count only the board gives that this board can't (the game's template card: your hand, your X) makes one.
                count = (CountOn(make, board, program) ?? 1) * PlayersByYou(make, board, program);
                unread |= !CountReadable(make);
            }
            made += count;
            switch (KindOf(make))
            {
                case Kind.Status: statuses += count; break;
                case Kind.Sadness: sadness += count; break;
            }
            if (IntoHand(make, type) && (make.For == null || StepValues.IsOwner(make.For) || make.By == Way.Forged)) intoHand += count;
        }
        return new OnBoard(made, statuses, sadness, intoHand, forged, unread);
    }

    /// <summary>
    /// The make's count can be read from its code: its count argument has nothing the reader couldn't follow or only a command's result gives,
    /// and the loop that filled its list goes round a readable number of times (CardEffects.RoundsReadable, or over a card value).
    /// </summary>
    public static bool CountReadable(Make make)
    {
        if (make.PerCall is { } perCall) return !perCall.Parts().Any(part => part is UnknownValue or ResultValue);
        return make.FilledBy is not { } loop || CardEffects.RoundsReadable(loop) || loop.Over is not ("?" or "") && !loop.Over.StartsWith("each of", StringComparison.Ordinal);
    }

    /// <summary>The players of your side a make in a loop over them makes cards for, as their creator: all of them when you are, else only you.</summary>
    private static int PlayersByYou(Make make, ProgramBoard board, ModelProgram program)
    {
        if (make.Found?.Loops.LastOrDefault(CardEffects.OverYourSide) is not { Items: { } players } || !StepValues.IsOwner(make.Creator) || board.Card.IsCanonical) return 1;
        return ProgramEvaluator.Items(players, board, program)?.Count ?? 1;
    }

    /// <summary>
    /// The card's owner has a Sovereign Blade in the fight outside the exhaust pile: a forge makes none then (ForgeCmd.Forge only adds
    /// damage). The game's template card (no owner) has none.
    /// </summary>
    public static bool HasBlade(CardModel card)
    {
        if (card.IsCanonical) return false;
        try
        {
            return card.Owner.PlayerCombatState?.AllCards.Any(other => other is MegaCrit.Sts2.Core.Models.Cards.SovereignBlade && !other.IsDupe && other.Pile?.Type != PileType.Exhaust) ?? true;
        }
        catch
        {
            return true;
        }
    }

    private static PileType? PileOf(ProgramValue? value) => value is ConstantValue { Value: var pile } ? (PileType)(int)pile : null;

    private static ProgramValue? Unstored(ProgramValue? value) => value is StoredValue stored ? Unstored(stored.Value) : value;

    /// <summary>How many cards one round of the make makes on the board (its loops' rounds aren't in it); null when its count can't be read.</summary>
    public static int? PerRound(Make make, ProgramBoard board, ModelProgram program)
    {
        if (make.FilledBy != null) return ProgramEvaluator.Rounds(make.FilledBy, board, program);
        if (make.PerCall == null) return 1;
        return ProgramEvaluator.Number(make.PerCall, board, program) is { } count ? Math.Max(0, (int)count) : null;
    }

    /// <summary>How many cards the make makes on the board: one round's times the rounds of its loops but the ones over your side; null when it can't be read.</summary>
    public static int? CountOn(Make make, ProgramBoard board, ModelProgram program)
    {
        if (PerRound(make, board, program) is not { } perRound) return null;
        var loops = make.Found?.Loops.Where(loop => !CardEffects.OverYourSide(loop)) ?? Enumerable.Empty<LoopStep>();
        return loops.Aggregate(perRound, (times, loop) => times * ProgramEvaluator.Rounds(loop, board, program));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Kind> Kinds = new();

    /// <summary>What the make's card is to the created-card hooks (its CardType: a status; a Sadness); any other card when it isn't named.</summary>
    public static Kind KindOf(Make make) => make.Card is not { } name ? Kind.Card : Kinds.GetOrAdd(name, cardName =>
    {
        if (cardName == nameof(MegaCrit.Sts2.Core.Models.Cards.Sadness)) return Kind.Sadness;
        var canonical = ModelDb.AllCards.FirstOrDefault(other => other.GetType().Name == cardName);
        return canonical?.Type == CardType.Status ? Kind.Status : Kind.Card;
    });

    /// <summary>
    /// The make's cards go into a hand: its pile is the hand, or not written as a constant where every pile the card's code writes is the hand
    /// (CardCode.MadeInto: Shiv.CreateInHand's, a helper's), as a forge's blade does.
    /// </summary>
    public static bool IntoHand(Make make, Type cardType) =>
        make.Pile == PileType.Hand || make.Pile == null && make.By != Way.AfterPlay && CardCode.MadeInto(cardType).All(pile => pile is null or PileType.Hand);
}

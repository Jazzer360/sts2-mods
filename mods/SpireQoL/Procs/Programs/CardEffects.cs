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
    public static bool HitsAllOnKill(Type cardType) => HitsAllOnKill(CardPrograms.Of(cardType));

    /// <summary>Kills your Osty (CreatureCmd.Kill on Owner.Osty: Sacrifice, Bone Shards).</summary>
    public static bool KillsOsty(CardProgram program) => program.Commands.Any(command => Is(command, "CreatureCmd", "Kill") && Argument(command, 0) == "Owner.Osty");

    /// <summary>Sets its target's HP (CreatureCmd.SetCurrentHp: Judgement's execute).</summary>
    public static bool SetsTargetsHp(CardProgram program) => program.Commands.Any(command => Is(command, "CreatureCmd", "SetCurrentHp") && Argument(command, 0) == Target);

    /// <summary>Takes its target's block (CreatureCmd.LoseBlock on the target: Expose).</summary>
    public static bool TakesTargetsBlock(CardProgram program) => program.Commands.Any(command => Is(command, "CreatureCmd", "LoseBlock") && Argument(command, 1) == Target);

    /// <summary>Damages its target with CreatureCmd.Damage rather than an attack (Capture Spirit).</summary>
    public static bool DamagesTargetWithoutAttacking(CardProgram program) =>
        program.Commands.Any(command => Is(command, "CreatureCmd", "Damage") && Argument(command, 1) == Target);

    /// <summary>Gains block for a creature that isn't you, outside a loop over players (Lift, Demonic Shield: its target; Constellation: the teammate it picked).</summary>
    public static bool BlocksAnother(CardProgram program) =>
        Outside(program.Steps).Any(command => Is(command, "CreatureCmd", "GainBlock") && Argument(command, 0) is { } gainer && gainer != You
                                             && (gainer == Target || gainer.EndsWith(".Creature", StringComparison.Ordinal)));

    /// <summary>Gains block for each creature a loop goes over (Rally: every living player), not for you or the target each round.</summary>
    public static bool BlocksEachPlayer(CardProgram program) =>
        program.All().OfType<LoopStep>().Any(loop => loop.Body.OfType<CommandStep>().Any(command => Is(command, "CreatureCmd", "GainBlock") && Argument(command, 0) is { } gainer
                                                                                                    && gainer != You && gainer != Target));

    /// <summary>Channels an orb for its target's player (Ignition: into a teammate's orbs).</summary>
    public static bool ChannelsForTarget(CardProgram program) =>
        program.Commands.Any(command => Is(command, "OrbCmd", "Channel") && command.Arguments.Contains(Target + ".Player"));

    /// <summary>A second attack, at every enemy, when the first one killed (Timber).</summary>
    public static bool HitsAllOnKill(CardProgram program)
    {
        var attacks = program.Commands.Where(command => Is(command, "DamageCmd", "Attack")).ToList();
        return attacks.Count >= 2 && attacks.Skip(1).Any(attack => attack.Condition != null && attack.Builder.Contains("TargetingAllOpponents"));
    }

    public static bool DrawsForTarget(Type cardType) => DrawsForTarget(CardPrograms.Of(cardType));
    public static bool DrawsForEachTeammate(Type cardType) => DrawsForEachTeammate(CardPrograms.Of(cardType));
    public static bool ShufflesDiscardPileIn(Type cardType) => ShufflesDiscardPileIn(CardPrograms.Of(cardType));
    public static bool ShufflesWhenShort(Type cardType) => ShufflesWhenShort(CardPrograms.Of(cardType));
    public static bool TakesIntoHand(Type cardType) => TakesIntoHand(CardPrograms.Of(cardType));
    public static bool PutsBackFromHand(Type cardType) => PutsBackFromHand(CardPrograms.Of(cardType));
    public static bool MovesCardsElsewhere(Type cardType) => MovesCardsElsewhere(CardPrograms.Of(cardType));

    // ---- draws and shuffles

    /// <summary>Draws for a player that isn't you, outside a loop over players (Constellation: CardPileCmd.Draw for its target's player).</summary>
    public static bool DrawsForTarget(CardProgram program) =>
        Outside(program.Steps).Any(command => IsDraw(command) && Argument(command, 2) is { } player && player != "Owner");

    /// <summary>Draws for each player a loop goes over, not you (Huddle Up: every teammate draws).</summary>
    public static bool DrawsForEachTeammate(CardProgram program) =>
        program.All().OfType<LoopStep>().Any(loop => loop.Body.OfType<CommandStep>().Any(command => IsDraw(command) && Argument(command, 2) is { } player && player != "Owner"));

    /// <summary>Shuffles your discard pile into your draw pile on every play (Reboot: CardPileCmd.Shuffle, no condition around it).</summary>
    public static bool ShufflesDiscardPileIn(CardProgram program) =>
        program.Commands.Any(command => Is(command, "CardPileCmd", "Shuffle") && command.Condition == null);

    /// <summary>Shuffles your discard pile in only on a condition (Ki Chant: when your draw pile is short of what it looks at).</summary>
    public static bool ShufflesWhenShort(CardProgram program) =>
        program.Commands.Any(command => Is(command, "CardPileCmd", "Shuffle")) && !ShufflesDiscardPileIn(program);

    private static bool IsDraw(CommandStep command) =>
        command.Command.DeclaringType == "CardPileCmd" && command.Command.Name.StartsWith("Draw", StringComparison.Ordinal);

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
    private static IEnumerable<Move> Moves(CardProgram program)
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
    public static bool TakesIntoHand(CardProgram program) => Moves(program).Contains(Move.IntoYourHand);

    /// <summary>Puts cards you pick from your hand back on your draw pile (Glimmer, Photon Cut, Thinking Ahead).</summary>
    public static bool PutsBackFromHand(CardProgram program) => Moves(program).Contains(Move.PutBack);

    /// <summary>
    /// Moves a card it picks somewhere your hand doesn't see: from your discard pile onto your draw pile (Headbutt, Recall, Cosmic
    /// Indifference), or into a teammate's hand (Tutor).
    /// </summary>
    public static bool MovesCardsElsewhere(CardProgram program) =>
        Moves(program).ToList() is var moves && moves.Contains(Move.Elsewhere) && !moves.Contains(Move.IntoYourHand) && !moves.Contains(Move.PutBack);

    public static bool HitsXTimes(Type cardType) => HitsXTimes(CardPrograms.Of(cardType));
    public static bool AttacksOnceRepeatingSomethingElse(Type cardType) => AttacksOnceRepeatingSomethingElse(CardPrograms.Of(cardType));

    // ---- attacks

    /// <summary>An attack whose hit count is the card's X (WithHitCount(ResolveEnergyXValue()): Whirlwind, Skewer, Volley).</summary>
    public static bool HitsXTimes(CardProgram program) =>
        program.Commands.Any(command => Is(command, "DamageCmd", "Attack") && command.HitCount == "ResolveEnergyXValue");

    /// <summary>
    /// Attacks once (no hit count) and repeats something else its Repeat times (Ice Lance channels Repeat Frost, Heirloom Hammer makes
    /// Repeat copies): its Repeat value isn't its hits.
    /// </summary>
    public static bool AttacksOnceRepeatingSomethingElse(CardProgram program) =>
        program.Commands.Any(command => Is(command, "DamageCmd", "Attack") && command.HitCount == null)
        && program.All().OfType<LoopStep>().Any(loop => loop.Over == "Repeat" && !new CardProgram("", loop.Body).Commands.Any(command => Is(command, "DamageCmd", "Attack")));

    public static bool AutoPlaysFromDrawPile(Type cardType) => AutoPlaysFromDrawPile(CardPrograms.Of(cardType));
    public static bool SummonsEachPlayer(Type cardType) => SummonsEachPlayer(CardPrograms.Of(cardType));
    public static bool GainsStarsOnKill(Type cardType) => GainsStarsOnKill(CardPrograms.Of(cardType));
    public static bool DislodgesArrows(Type cardType) => DislodgesArrows(CardPrograms.Of(cardType));
    public static bool KeepsStretchForReplays(Type cardType) => KeepsStretchForReplays(CardPrograms.Of(cardType));

    public static bool TakesWholeHand(Type cardType) => TakesWholeHand(CardPrograms.Of(cardType));

    /// <summary>
    /// Takes every card in your hand, whatever it is: a loop over your hand's cards that exhausts, discards or moves each (Fiend Fire,
    /// The Devil, Reboot), or a discard of the hand's cards (Storm of Steel, Calculated Gamble). Not a loop over some of them (Second
    /// Wind's non-Attacks, Growth's Sadness: a filter, Where).
    /// </summary>
    public static bool TakesWholeHand(CardProgram program) =>
        program.All().OfType<LoopStep>().Any(loop => IsWholeHand(loop.Over.Replace("each of ", ""))
                                                     && loop.Body.OfType<CommandStep>().Any(command => TakesFromHand(command)))
        || program.Commands.Any(command => command.Command.DeclaringType == "CardCmd" && command.Command.Name is "Discard" or "DiscardAndDraw"
                                           && IsWholeHand(Argument(command, 1)));

    private static bool IsWholeHand(string? cards) => cards is "Hand.Cards" or "Hand.Cards.ToList";

    private static bool TakesFromHand(CommandStep command) =>
        command.Command.DeclaringType == "CardCmd" && command.Command.Name is "Exhaust" or "Discard" || Is(command, "CardPileCmd", "Add");

    // ---- the rest

    /// <summary>Plays cards from the top of your draw pile (CardPileCmd.AutoPlayFromDrawPile: Havoc, Cascade, The Devil).</summary>
    public static bool AutoPlaysFromDrawPile(CardProgram program) => program.Commands.Any(command => Is(command, "CardPileCmd", "AutoPlayFromDrawPile"));

    /// <summary>Summons each player's Osty, a loop over the players (Legion of Bone: OstyCmd.Summon for each, not only you).</summary>
    public static bool SummonsEachPlayer(CardProgram program) =>
        program.All().OfType<LoopStep>().Any(loop => loop.Body.OfType<CommandStep>().Any(command => Is(command, "OstyCmd", "Summon") && Argument(command, 1) is { } player && player != "Owner"));

    /// <summary>Gains stars only when its attack kills (Knockout Blow: PlayerCmd.GainStars behind a test of the attack's results).</summary>
    public static bool GainsStarsOnKill(CardProgram program)
    {
        var attacked = false;
        foreach (var command in program.Commands)
        {
            if (Is(command, "DamageCmd", "Attack")) attacked = true;
            else if (attacked && Is(command, "PlayerCmd", "GainStars") && command.Condition != null) return true;
        }
        return false;
    }

    /// <summary>Knocks your arrows out of its target (ArrowCmd.DislodgeArrow on the target: Recall, Uproot, Wrest).</summary>
    public static bool DislodgesArrows(CardProgram program) => program.Commands.Any(command => Is(command, "ArrowCmd", "DislodgeArrow") && Argument(command, 0) == Target);

    /// <summary>
    /// Gains block, then gives you Stretch (Cho Ki): the Stretch is for a replay's later plays, so this play's block doesn't spend it.
    /// </summary>
    public static bool KeepsStretchForReplays(CardProgram program)
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
    public static bool MakesSadness(CardProgram program) => program.All().OfType<CreateStep>().Any(create => create.Card == nameof(MegaCrit.Sts2.Core.Models.Cards.Sadness));

    /// <summary>Forges (ForgeCmd.Forge: a Sovereign Blade made the first time, more damage after).</summary>
    public static bool Forges(CardProgram program) => program.Commands.Any(command => Is(command, "ForgeCmd", "Forge"));

    public static bool DiscardsOnlyPicks(Type cardType) => DiscardsOnlyPicks(CardPrograms.Of(cardType));

    /// <summary>
    /// Every card it discards is one you pick: each CardCmd.Discard (or DiscardAndDraw) follows a choice (CardSelectCmd) in its code
    /// (Acrobatics, Prepared, The Hermit), not the whole hand (Storm of Steel, Calculated Gamble). A pick is a card, never a particular
    /// one (decision #92): what only a particular card would do then (a Sly card playing itself) isn't followed.
    /// </summary>
    public static bool DiscardsOnlyPicks(CardProgram program)
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

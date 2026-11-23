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

    /// <summary>Gains block for each creature a loop goes over (Rally: every living player).</summary>
    public static bool BlocksEachPlayer(CardProgram program) =>
        program.All().OfType<LoopStep>().Any(loop => loop.Body.OfType<CommandStep>().Any(command => Is(command, "CreatureCmd", "GainBlock") && Argument(command, 0) is { } gainer
                                                                                                    && gainer.StartsWith("ld", StringComparison.Ordinal)));

    /// <summary>Channels an orb for its target's player (Ignition: into a teammate's orbs).</summary>
    public static bool ChannelsForTarget(CardProgram program) =>
        program.Commands.Any(command => Is(command, "OrbCmd", "Channel") && command.Arguments.Contains(Target + ".Player"));

    /// <summary>A second attack, at every enemy, when the first one killed (Timber).</summary>
    public static bool HitsAllOnKill(CardProgram program)
    {
        var attacks = program.Commands.Where(command => Is(command, "DamageCmd", "Attack")).ToList();
        return attacks.Count >= 2 && attacks.Skip(1).Any(attack => attack.Condition != null && attack.Builder.Contains("TargetingAllOpponents"));
    }

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

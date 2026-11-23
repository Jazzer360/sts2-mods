using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// A play's actions in the order the card's code runs them (decision #182, docs/CARD_PROGRAMS.md): each action stands for the game
/// command that does it, and the card's program (read from its code) says in which order those commands run. What a command sets
/// off runs inside its action, so the order is what decides, for instance, whether Iron Wave's block is there when Thorns answers
/// its attack. A loop that does several things per round (Second Wind: exhaust, then block, for each card) does them round by
/// round. An action no command stands for keeps its place after the action it followed.
/// </summary>
internal static class PlayOrder
{
    /// <summary>The card's program (empty when its code can't be read: the actions keep their order).</summary>
    public static CardProgram ProgramOf(Type cardType) => CardPrograms.Of(cardType);

    /// <summary>The game command an action stands for (a key also given to the program's commands), or null for one that stands for none.</summary>
    internal static string? KeyOf(SimAction action) => action switch
    {
        AttackAction => "attack",
        GainBlockAction => "block",
        ApplyPowerAction apply => "power:" + apply.Power.Name,
        FocusAction => "power:FocusPower",
        OrbAction => "orb",
        LoseHpAction => "self-damage",
        DamageAction => "damage",
        StripBlockAction => "lose-block",
        ExecuteAction => "execute",
        KillOstyAction => "kill",
        ShootAction => "shoot",
        ImpaleAction => "impale",
        DislodgeAllAction => "dislodge",
        TakeToHandAction => "to-hand",
        AutoPlayFromDrawAction => "auto-play",
        DrawnCardAction => "draw",
        CardEventAction { Event: CardEvent.Drawn } => "draw",
        CardEventAction { Event: CardEvent.Exhausted } => "exhaust",
        CardEventAction { Event: CardEvent.Discarded } => "discard",
        CardEventAction { Event: CardEvent.Generated or CardEvent.GeneratedStatus or CardEvent.GeneratedSadness } => "generate",
        CardEventAction { Event: CardEvent.EnergyGained } => "energy",
        CardEventAction { Event: CardEvent.StarsGained } => "stars",
        _ => null,
    };

    /// <summary>The keys a program's command stands for (DiscardAndDraw is both).</summary>
    internal static IEnumerable<string> KeysOf(CommandStep step)
    {
        var command = step.Command;
        switch (command.DeclaringType, command.Name)
        {
            case ("DamageCmd", "Attack"): yield return "attack"; break;
            case ("CreatureCmd", "GainBlock"): yield return "block"; break;
            case ("CreatureCmd", "LoseBlock"): yield return "lose-block"; break;
            case ("CreatureCmd", "Damage"):
                // Its target: the card's owner (Offering, Bloodletting) or another creature (the first argument after the choice context).
                yield return step.Arguments.Skip(1).FirstOrDefault() == "Creature" ? "self-damage" : "damage";
                break;
            case ("CreatureCmd", "Kill"): yield return "kill"; break;
            case ("CreatureCmd", "SetCurrentHp"): yield return "execute"; break;
            case ("PowerCmd", "Apply"): yield return "power:" + (command.GenericArguments.Count > 0 ? command.GenericArguments[0] : ""); break;
            case ("CardPileCmd", "Draw" or "DrawWithoutBlockingOnOtherPlayers"): yield return "draw"; break;
            case ("CardPileCmd", "AddGeneratedCardToCombat" or "AddGeneratedCardsToCombat"): yield return "generate"; break;
            case ("CardPileCmd", "Add"): yield return "to-hand"; break;
            case ("CardPileCmd", "AutoPlayFromDrawPile"): yield return "auto-play"; break;
            case ("CardCmd", "AutoPlay"): yield return "auto-play"; break;
            case ("CardCmd", "Exhaust"): yield return "exhaust"; break;
            case ("CardCmd", "Discard"): yield return "discard"; break;
            case ("CardCmd", "DiscardAndDraw"): yield return "discard"; yield return "draw"; break;
            case ("CardCmd", "Transform" or "TransformTo"): yield return "generate"; break;
            // The first forge makes the Sovereign Blade (through AddGeneratedCardToCombat): the card it creates.
            case ("ForgeCmd", "Forge"): yield return "generate"; break;
            case ("PlayerCmd", "GainEnergy"): yield return "energy"; break;
            case ("PlayerCmd", "GainStars"): yield return "stars"; break;
            case ("OrbCmd", _): yield return "orb"; break;
            case ("ArrowCmd", "ShootArrowAtTarget" or "TryShootArrow"): yield return "shoot"; break;
            case ("ArrowCmd", "ImpaleTarget"): yield return "impale"; break;
            case ("ArrowCmd", "DislodgeArrow"): yield return "dislodge"; break;
            default: yield return command.FullName; break;
        }
    }

    /// <summary>
    /// <paramref name="actions"/> put in the program's order. A <see cref="LeaveHandAction"/> or an exhaust pick goes with the
    /// event that follows it (the cards leave, then their exhausts or discards set things off).
    /// </summary>
    public static List<SimAction> Arrange(IReadOnlyList<SimAction> actions, CardProgram program)
    {
        var steps = Steps(program);
        if (steps.Count == 0 || actions.Count < 2) return actions.ToList();
        // Each action's step: the first step of its key still free for it; actions of one key share a step only when the program
        // has fewer steps of that key than the script has actions (a power applied to every enemy is one command).
        var stepOf = new int[actions.Count];
        var used = new Dictionary<string, int>();
        for (var i = 0; i < actions.Count; i++)
        {
            stepOf[i] = -1;
            if (KeyOf(actions[i]) is not { } key) continue;
            var matching = steps.Select((step, index) => (step, index)).Where(entry => entry.step.Keys.Contains(key) || key.StartsWith("power:", StringComparison.Ordinal) && entry.step.Keys.Contains("power:" + PowerTypeName(key))).Select(entry => entry.index).ToList();
            if (matching.Count == 0 && key.StartsWith("power:", StringComparison.Ordinal))
                matching = steps.Select((step, index) => (step, index)).Where(entry => entry.step.Keys.Any(stepKey => stepKey.StartsWith("power:", StringComparison.Ordinal))).Select(entry => entry.index).ToList();
            if (matching.Count == 0) continue;
            var nth = used.GetValueOrDefault(key);
            stepOf[i] = matching[Math.Min(nth, matching.Count - 1)];
            used[key] = nth + 1;
        }
        // Cards leaving and an exhaust pick belong to the event after them.
        for (var i = actions.Count - 2; i >= 0; i--)
            if (actions[i] is LeaveHandAction or ExhaustFromHandAction && stepOf[i] < 0) stepOf[i] = stepOf[i + 1];
        // An action standing for no command stays after the one before it.
        var sortKey = new (int Step, int Round, int Index)[actions.Count];
        var last = (Step: -1, Round: 0, Index: -1);
        for (var i = 0; i < actions.Count; i++)
        {
            sortKey[i] = stepOf[i] >= 0 ? (steps[stepOf[i]].Order, 0, i) : last with { Index = i };
            if (stepOf[i] >= 0) last = sortKey[i];
        }
        var pieces = new List<(SimAction Action, (int Step, int Round, int Index) Key)>();
        for (var i = 0; i < actions.Count; i++)
        {
            var loop = stepOf[i] >= 0 ? steps[stepOf[i]].Loop : null;
            if (loop != null && RoundsOf(actions, stepOf, loop, steps) is { } rounds)
            {
                // Round by round: the loop's first step's order, then each round, then the step's place in the loop's body. What
                // picks the cards (an exhaust pick) comes before the first round.
                if (Split(actions[i], rounds) is not { } split)
                {
                    pieces.Add((actions[i], (steps[loop.First].Order, 0, i)));
                    continue;
                }
                for (var round = 0; round < rounds; round++)
                    pieces.Add((split[round], (steps[loop.First].Order, round + 1, steps[stepOf[i]].Order * 10000 + i)));
                continue;
            }
            pieces.Add((actions[i], sortKey[i]));
        }
        return pieces.OrderBy(piece => piece.Key.Step).ThenBy(piece => piece.Key.Round).ThenBy(piece => piece.Key.Index).Select(piece => piece.Action).ToList();
    }

    /// <summary>"power:ThornsPower" → "ThornsPower".</summary>
    private static string PowerTypeName(string key) => key["power:".Length..];

    private sealed record Step(int Order, HashSet<string> Keys, LoopInfo? Loop);

    /// <summary>A loop's steps (their indexes in the flattened list).</summary>
    private sealed record LoopInfo(int First, int Last);

    /// <summary>The program's commands flattened in order, each with the loop it is in (the innermost that has two or more commands).</summary>
    private static List<Step> Steps(CardProgram program)
    {
        var steps = new List<Step>();
        void Walk(IEnumerable<ProgramStep> programSteps, LoopInfo? loop)
        {
            foreach (var programStep in programSteps)
            {
                switch (programStep)
                {
                    case CommandStep command:
                        steps.Add(new Step(steps.Count, KeysOf(command).ToHashSet(), loop));
                        if (command.Inner.Count > 0) Walk(command.Inner, loop);
                        break;
                    case LoopStep inner:
                        var first = steps.Count;
                        var commands = new CardProgram("", inner.Body).Commands.Count();
                        // Only a loop that does two or more things per round needs its rounds kept apart.
                        var info = commands >= 2 ? new LoopInfo(first, first + commands - 1) : loop;
                        Walk(inner.Body, info);
                        break;
                }
            }
        }
        Walk(program.Steps, null);
        return steps;
    }

    /// <summary>
    /// How many rounds a loop's actions split into: every action matched to the loop splits into the same number of one-round
    /// pieces (n cards exhausted, n block gains). Null when they don't (the actions stay whole, in step order).
    /// </summary>
    private static int? RoundsOf(IReadOnlyList<SimAction> actions, int[] stepOf, LoopInfo loop, List<Step> steps)
    {
        int? rounds = null;
        var keys = new HashSet<string>();
        for (var i = 0; i < actions.Count; i++)
        {
            if (stepOf[i] < loop.First || stepOf[i] > loop.Last || actions[i] is ExhaustFromHandAction) continue;
            var count = Rounds(actions[i]);
            if (count is not > 0) return null;
            if (rounds != null && rounds != count) return null;
            rounds = count;
            foreach (var key in steps[stepOf[i]].Keys) keys.Add(key);
        }
        return keys.Count >= 2 && rounds > 1 ? rounds : null;
    }

    /// <summary>How many times an action does its thing, when it can be split into that many single ones.</summary>
    private static int? Rounds(SimAction action) => action switch
    {
        LeaveHandAction { Whole: false } leave => leave.Count,
        CardEventAction { Card: null } cardEvent => cardEvent.Count,
        GainBlockAction block => block.Times,
        _ => null,
    };

    private static List<SimAction>? Split(SimAction action, int rounds) => action switch
    {
        LeaveHandAction leave => Enumerable.Repeat<SimAction>(leave with { Count = 1 }, rounds).ToList(),
        CardEventAction cardEvent => Enumerable.Repeat<SimAction>(cardEvent with { Count = 1 }, rounds).ToList(),
        GainBlockAction block => Enumerable.Repeat<SimAction>(block with { Times = 1 }, rounds).ToList(),
        _ => null,
    };
}

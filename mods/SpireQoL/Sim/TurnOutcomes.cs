namespace SpireQoL.Sim;

/// <summary>
/// One way the enemy turn can go for you (decision #155): every branch that leaves you the same (HP lost, dead or not, what saved
/// you), their chance added up. Branch is the most likely of them: its breakdown is what Incoming Damage shows for this outcome.
/// Share is its part of the branches' weight, for ordering; Chance is the same as a SimChance (a range while the search isn't done).
/// </summary>
public sealed record TurnOutcome(int Taken, int UnblockableTaken, bool Dies, int FairiesUsed, bool LizardTailUsed, int HpAfter, SimChance Chance, double Share, SimState Branch)
{
    /// <summary>Fairy in a Bottle or Lizard Tail brought you back, and nothing killed you after.</summary>
    public bool Revived => !Dies && (FairiesUsed > 0 || LizardTailUsed);
}

/// <summary>The ways an enemy turn's outcome can go for you, from the simulator's branches (decision #155).</summary>
public static class TurnOutcomes
{
    /// <summary>
    /// Every outcome, the most likely first; on equal chances the worse for you first (you die, then more HP lost), so a tie never
    /// shows the kinder one. <paramref name="hpIfUnknown"/>: your HP for a branch that doesn't track it.
    /// </summary>
    public static IReadOnlyList<TurnOutcome> Of(SimOutcome outcome, int hpIfUnknown)
    {
        var branches = outcome.Branches;
        if (branches.Count == 0) return Array.Empty<TurnOutcome>();
        var total = branches.Sum(branch => branch.Weight);
        double Share(IEnumerable<SimState> group) => total > 0 ? group.Sum(branch => branch.Weight) / total : (double)group.Count() / branches.Count;
        return branches
            .GroupBy(branch => (branch.Taken, branch.Died, branch.FairiesUsed, branch.LizardTailUsed))
            .Where(group => total <= 0 || Share(group) > 0) // a branch with no chance (an empty group of cards) isn't an outcome
            .Select(group =>
            {
                var (taken, died, fairies, tail) = group.Key;
                var branch = group.OrderByDescending(member => member.Weight).First();
                return new TurnOutcome(taken, Math.Min(branch.UnblockableTaken, taken), died, fairies, tail, branch.You.Hp == int.MaxValue ? hpIfUnknown : branch.You.Hp,
                    outcome.Chance(member => (member.Taken, member.Died, member.FairiesUsed, member.LizardTailUsed) == group.Key), Share(group), branch);
            })
            .OrderByDescending(turnOutcome => Math.Round(turnOutcome.Share, 9)).ThenByDescending(turnOutcome => turnOutcome.Dies).ThenByDescending(turnOutcome => turnOutcome.Taken)
            .ToList();
    }

    /// <summary>
    /// What makes one outcome different from another, from their breakdowns: each source whose damage (or end-of-turn block)
    /// differs, "Burn +2", "Mecha Knight −12", "I Am Invincible block +10". The first three.
    /// </summary>
    public static string Differences(SimState branch, SimState from)
    {
        var parts = new List<string>();
        void Compare(List<(string Name, int Amount)> sources, List<(string Name, int Amount)> fromSources, string what)
        {
            var amounts = sources.GroupBy(source => source.Name).ToDictionary(group => group.Key, group => group.Sum(source => source.Amount));
            var fromAmounts = fromSources.GroupBy(source => source.Name).ToDictionary(group => group.Key, group => group.Sum(source => source.Amount));
            foreach (var name in amounts.Keys.Union(fromAmounts.Keys))
            {
                var difference = amounts.GetValueOrDefault(name) - fromAmounts.GetValueOrDefault(name);
                if (difference != 0) parts.Add($"{name}{what} {(difference > 0 ? "+" : "−")}{Math.Abs(difference)}");
            }
        }
        Compare(branch.Blockable, from.Blockable, "");
        Compare(branch.Unblockable, from.Unblockable, "");
        Compare(branch.EndOfTurnBlock, from.EndOfTurnBlock, " block");
        return string.Join(", ", parts.Take(3));
    }
}

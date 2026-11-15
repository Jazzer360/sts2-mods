namespace SpireQoL.Sim;

/// <summary>
/// One step of a card's script (docs/SIMULATOR_V2.md "Card scripts"). The engine runs a script's actions in order on every
/// branch; an action may push hooks and branch. Actions are immutable values shared by every branch.
/// </summary>
public abstract record SimAction;

/// <summary>
/// A part of a play given as a SimPlay summary (its precomputed numbers), run as it always was. Plays built from SimPlay
/// (tests, the golden scenarios) compile to these; the adapter's plays move to real actions step by step.
/// </summary>
public sealed record LegacyPart(SimPlay Play, LegacyPart.Kind Part) : SimAction
{
    public enum Kind
    {
        Setup, HpCost, Hits, AfterHits, Orbs, Block, AfterBlock, Resolve, RandomPoison, Procs, Deaths,
    }
}

/// <summary>A script: the actions a play (or an auto-play, or a lead-in play) runs.</summary>
public sealed class SimScript
{
    public string Name = "";
    public List<SimAction> Actions = new();

    public static SimScript Of(SimPlay p)
    {
        var s = new SimScript { Name = p.Name };
        // v2: the card play first (each play's body with its hooks), then the summary's remaining parts.
        if (p.Card != null) s.Actions.Add(new CardPlayAction(p.Card, p.Body));
        foreach (var part in Enum.GetValues<LegacyPart.Kind>()) s.Actions.Add(new LegacyPart(p, part));
        return s;
    }
}

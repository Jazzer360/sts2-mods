namespace SpireQoL.Sim;

/// <summary>
/// A tiny made-up fight run once a launch on the load's worker (decision #254), so the engine's paths a fight's first frames take (the turn
/// forecast, a card's play and its search, a state's key) have run before the first fight does: compiling a method doesn't make the generic
/// code it uses on value types, nor set the engine's own first-use state. Simulator code only, on its own state: no game object is read.
/// </summary>
public static class SimWarmUp
{
    /// <summary>Runs the made-up fight: the turn ended now, then a Strike-like play and a Defend-like play searched to the enemy turn.</summary>
    /// <returns>How many outcomes the runs gave (for the dev log; nothing reads them).</returns>
    public static int Run()
    {
        var start = new SimState { TurnNumber = 1 };
        start.You.Hp = start.You.MaxHp = 50;
        start.You.Energy = 3;
        start.You.HandCount = 5;
        // A power on each side and a draw pile, so the state's key writes what a fight's does.
        start.You.Powers = start.You.Powers.Add(PowerId.Vulnerable, delta: 1);
        start.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), Count: 5) }, Discard = { new SimDrawGroup(new SimDrawCard("plain"), Count: 1) } };
        start.Enemies.Add(new SimEnemy { Id = 0, Name = "warm-up", Hp = 40, Hits = { 6 }, IntendsToAttack = true, Powers = PowerSet.Empty.Add(PowerId.Strength, delta: 1) });
        var outcomes = SimEngine.Run(start).Branches.Count;
        outcomes += new SimSearch(start, Play("WarmUpStrike", SimCardType.Attack, target: 0, new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 6, SimProps.Move))))
            .SearchAll().Branches.Count;
        outcomes += new SimSearch(start, Play("WarmUpDefend", SimCardType.Skill, target: Who.None, new GainBlockAction(Times: 1, Base: 5, SimProps.Move)))
            .SearchAll().Branches.Count;
        _ = start.FullKey();
        return outcomes;
    }

    private static SimScript Play(string name, SimCardType type, int target, SimAction body) => new()
    {
        Name = name,
        Actions = { new CardPlayAction(new SimCardPlay(new SimCard(name, type), Who.You, target, PlayIndex: 0, PlayCount: 1, AutoPlay: false), new[] { body }) },
    };
}

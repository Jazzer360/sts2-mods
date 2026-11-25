namespace SpireQoL.Sim.Tests;

/// <summary>Builds simulator scripts for tests (docs/SIMULATOR.md): a card play with its body actions, and the states they leave.</summary>
internal static class ScriptBuilder
{
    public static SimCard Card(string name, SimCardType type = SimCardType.Attack, params SimCardTag[] tags) =>
        new(name, type) { Tags = tags.ToHashSet() };

    public static SimScript Play(SimCard card, int target, int plays, params SimAction[] body) => new()
    {
        Name = card.Name,
        Actions = { new CardPlayAction(new SimCardPlay(card, Who.You, target, PlayIndex: 0, plays, AutoPlay: false), body) },
    };

    public static AttackAction Attack(decimal baseDamage, int hits = 1, SimPlay.Target mode = SimPlay.Target.Single, SimProps props = SimProps.Move,
        CapturedTerms? terms = null) =>
        new(hits, mode, new DamageSpec(baseDamage, props) { Captured = terms == null ? null : new AllTargets(terms) });

    public static GainBlockAction Block(decimal baseBlock, int times = 1) => new(times, baseBlock, SimProps.Move);

    public static SimEnemy Enemy(int id, int hp, int block = 0) => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block };

    /// <summary>
    /// An enemy out of the way (100 HP, no block, no hits, no powers): on a board that means the fight goes on, it stays alive so the others'
    /// deaths don't end the fight (decision #301: no primary enemy alive ends it, and every command after that does nothing). It can't be hit
    /// (SimEnemy.Hittable false, as the game's IsHittable is for an Illusion: not among CombatState.HittableEnemies), so no random hit, hit to every
    /// enemy or power on every hittable enemy reaches it: it's in no branch and no outcome of the play.
    /// </summary>
    public static SimEnemy Bystander(int id) => new() { Id = id, Name = $"E{id}", Hp = 100, Hittable = false };

    /// <summary>The board with a <see cref="Bystander"/> added after its enemies (the next id): the fight goes on past their deaths.</summary>
    public static SimState WithBystander(SimState state)
    {
        state.Enemies.Add(Bystander(id: state.Enemies.Count == 0 ? 0 : state.Enemies.Max(enemy => enemy.Id) + 1));
        return state;
    }

    /// <summary>The state after running a script (one branch expected).</summary>
    public static List<SimState> After(SimState state, SimScript script)
    {
        SimEngine.RunScriptTo(state, script, out var branches, out _);
        return branches;
    }

    /// <summary>Captured terms that are the same for every target.</summary>
    private sealed class AllTargets : IReadOnlyDictionary<int, CapturedTerms>
    {
        private readonly CapturedTerms _terms;
        public AllTargets(CapturedTerms terms) => _terms = terms;
        public CapturedTerms this[int key] => _terms;
        public IEnumerable<int> Keys => Enumerable.Range(0, 16);
        public IEnumerable<CapturedTerms> Values => Keys.Select(_ => _terms);
        public int Count => 16;
        public bool ContainsKey(int key) => true;
        public bool TryGetValue(int key, out CapturedTerms value) { value = _terms; return true; }
        public IEnumerator<KeyValuePair<int, CapturedTerms>> GetEnumerator() => Keys.Select(key => new KeyValuePair<int, CapturedTerms>(key, _terms)).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace SpireQoL.Sim.Tests;

/// <summary>Builds simulator scripts for tests (docs/SIMULATOR.md): a card play with its body actions, and the states they leave.</summary>
internal static class ScriptBuilder
{
    public static SimCard Card(string name, SimCardType type = SimCardType.Attack, params SimCardTag[] tags) =>
        new(name, type) { Tags = tags.ToHashSet() };

    public static SimScript Play(SimCard card, int target, int plays, params SimAction[] body) => new()
    {
        Name = card.Name,
        Actions = { new CardPlayAction(new SimCardPlay(card, Who.You, target, 0, plays, false), body) },
    };

    public static AttackAction Attack(decimal baseDamage, int hits = 1, SimPlay.Target mode = SimPlay.Target.Single, SimProps props = SimProps.Move,
        CapturedTerms? terms = null) =>
        new(hits, mode, new DamageSpec(baseDamage, props) { Captured = terms == null ? null : new AllTargets(terms) });

    public static GainBlockAction Block(decimal baseBlock, int times = 1) => new(times, baseBlock, SimProps.Move);

    public static SimEnemy Enemy(int id, int hp, int block = 0) => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block };

    /// <summary>The state after running a script (one branch expected).</summary>
    public static List<SimState> After(SimState state, SimScript script)
    {
        SimEngine.RunScriptTo(state, script, out var branches, out _);
        return branches;
    }

    /// <summary>Captured terms that are the same for every target.</summary>
    private sealed class AllTargets : IReadOnlyDictionary<int, CapturedTerms>
    {
        private readonly CapturedTerms _t;
        public AllTargets(CapturedTerms terms) => _t = terms;
        public CapturedTerms this[int key] => _t;
        public IEnumerable<int> Keys => Enumerable.Range(0, 16);
        public IEnumerable<CapturedTerms> Values => Keys.Select(_ => _t);
        public int Count => 16;
        public bool ContainsKey(int key) => true;
        public bool TryGetValue(int key, out CapturedTerms value) { value = _t; return true; }
        public IEnumerator<KeyValuePair<int, CapturedTerms>> GetEnumerator() => Keys.Select(key => new KeyValuePair<int, CapturedTerms>(key, _t)).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

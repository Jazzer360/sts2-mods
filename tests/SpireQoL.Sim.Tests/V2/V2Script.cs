using SpireQoL.Sim;

namespace SpireQoL.Sim.Tests.V2;

/// <summary>Builds v2 scripts for tests (docs/SIMULATOR_V2.md): a card play with its body actions.</summary>
internal static class V2Script
{
    public static SimCard Card(string name, SimCardType type = SimCardType.Attack, params string[] tags) =>
        new(name, type) { Tags = tags.ToHashSet() };

    public static SimScript Play(SimCard card, int target, int plays, params SimAction[] body) => new()
    {
        Name = card.Name,
        Actions = { new PlayCardAction(new SimCardPlay(card, Who.You, target, 0, plays, false), body) },
    };

    public static AttackAction Attack(decimal baseDamage, int hits = 1, SimPlay.Target mode = SimPlay.Target.Single, SimProps props = SimProps.Move,
        CapturedTerms? terms = null) =>
        new(hits, mode, new DamageSpec(baseDamage, props) { Captured = terms == null ? null : new AllTargets(terms) });

    public static GainBlockAction Block(decimal baseBlock, int times = 1) => new(times, baseBlock, SimProps.Move);

    public static SimEnemy Enemy(int id, int hp, int block = 0) => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block };

    /// <summary>The state after running a script (one branch expected).</summary>
    public static List<SimState> After(SimState s, SimScript script)
    {
        SimEngine.RunScriptTo(s, script, out var branches, out _);
        return branches;
    }

    /// <summary>Captured terms that are the same for every target.</summary>
    private sealed class AllTargets : IReadOnlyDictionary<int, CapturedTerms>
    {
        private readonly CapturedTerms _t;
        public AllTargets(CapturedTerms t) => _t = t;
        public CapturedTerms this[int key] => _t;
        public IEnumerable<int> Keys => Enumerable.Range(0, 16);
        public IEnumerable<CapturedTerms> Values => Keys.Select(_ => _t);
        public int Count => 16;
        public bool ContainsKey(int key) => true;
        public bool TryGetValue(int key, out CapturedTerms value) { value = _t; return true; }
        public IEnumerator<KeyValuePair<int, CapturedTerms>> GetEnumerator() => Keys.Select(k => new KeyValuePair<int, CapturedTerms>(k, _t)).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

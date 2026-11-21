using System.Collections;
using System.Reflection;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Branches merge when their keys match (SimState.Key, decision #107), so a field the key leaves out is one two branches can
/// differ in and still merge, the first one's value winning. Every field of the state, its players, enemies and orbs is changed
/// here one at a time on a copy: the key must change, or the field is listed below with why it may stay out. A new field fails
/// until it's in the key or on the list.
/// </summary>
public class SimKeyCoverageTests
{
    private const string Constant = "never differs between branches of one run (read from the game, or set once)";
    private const string Merged = "a per-play result merged on purpose: the guarantee keeps the smallest, the possibility the largest (MergeResults)";
    private const string Scratch = "the engine's scratch inside one step: set and cleared before branches are compared";
    private const string Bookkeeping = "how the search is run, not what happened";

    /// <summary>Fields that may stay out of the key: "Type.Field" and why.</summary>
    private static readonly Dictionary<string, string> NotKeyed = new()
    {
        // The search itself.
        ["SimState.Weight"] = "the branch's share of the rolls: identical branches add theirs when they merge",
        ["SimState.Log"] = Bookkeeping, ["SimState.Work"] = Bookkeeping, ["SimState.ListenerCache"] = Bookkeeping, ["SimState.ListenerSets"] = Bookkeeping,
        ["SimState._teammatesShared"] = Bookkeeping, ["SimPlayer._orbsShared"] = Bookkeeping,
        // Read from the game once and never changed.
        ["SimState.Vars"] = Constant, ["SimState.Titles"] = Constant, ["SimState.TeammatesBeforeMe"] = Constant,
        ["SimEnemy.Name"] = Constant, ["SimEnemy.Hittable"] = Constant, ["SimEnemy.Imbalanced"] = Constant, ["SimEnemy.Intent"] = Constant,
        ["SimEnemy.TeammateHits"] = Constant, ["SimEnemy.StartedAlive"] = Constant, ["SimEnemy.CrabRageBlock"] = Constant,
        ["SimEnemy.Explosive"] = Constant, ["SimEnemy.ExplosiveOnTeammates"] = Constant, ["SimEnemy.PoisonTriggers"] = Constant,
        ["SimPlayer.Reflect"] = Constant, ["SimPlayer.SlotOnFirstChannel"] = Constant, ["SimPlayer.FrontOrbBonusTriggers"] = Constant,
        ["SimPlayer.OstyShields"] = Constant, ["SimPlayer.FairyHeal"] = Constant, ["SimPlayer.BeaconOfHope"] = Constant,
        ["SimPlayer.Stampede"] = Constant, ["SimPlayer.Simpleton"] = Constant, ["SimPlayer.ExhaustAutoPlays"] = Constant,
        ["SimOrb.Triggers"] = Constant,
        // Per-play results, merged.
        ["SimState.PlayHpReached"] = Merged, ["SimState.PlayHpLosses"] = Merged, ["SimState.PlayHpLost"] = Merged,
        ["SimState.PlayBiggestHit"] = Merged, ["SimState.Fired"] = Merged, ["SimState.FiredAny"] = Merged,
        // Inside one step only.
        ["SimState.IndirectNow"] = Scratch, ["SimState.CurrentPlay"] = Scratch, ["SimState.AttackSerial"] = Scratch,
        ["SimState.CurrentAttack"] = Scratch, ["SimState.PlayFromHand"] = Scratch, ["SimState.EnemyTurnNow"] = Scratch,
        ["SimState.EndOfTurnNow"] = Scratch, ["SimState.Bound"] = Scratch, ["SimState.Pending"] = Scratch, ["SimState.ArrowAwayAt"] = Scratch,
        ["SimState.EventCard"] = Scratch, ["SimState.PlayEvents"] = Scratch, ["SimState.PlayCardBlockGains"] = Scratch,
        ["SimState.DrawRecord"] = "in the key while it's set (as a set: decision #120); null between steps",
        ["SimState.FirstHits"] = "the damage-formula audit's first hit on each enemy: the outcome keeps it only when every branch has the same",
        // Display logs: the first merged branch's breakdown is shown (Incoming Damage's own run).
        ["SimState.EndOfTurnBlock"] = "Incoming Damage's breakdown: the first merged branch's", ["SimState.Blockable"] = "Incoming Damage's breakdown",
        ["SimState.Unblockable"] = "Incoming Damage's breakdown", ["SimState.EndOfTurnDamage"] = "Incoming Damage's breakdown",
        ["SimState.FullBlockNeeded"] = "Imbalanced's threshold, set during the enemy turn after the last branching step",
        ["SimState.BlockAtEnemyTurn"] = "your block as the enemy turn starts: Block itself is in the key",
        ["SimEnemy.HpAtTurnEnd"] = "a copy of Hp taken once your end of turn is over (Hp is in the key)",
        ["SimEnemy.UnblockedAtTurnEnd"] = "a copy of UnblockedInstances taken once your end of turn is over (that is in the key)",
        ["SimEnemy.DeathHandled"] = "follows Hp: a dead enemy's death is handled in the same step",
        ["SimEnemy.StunnedThisTurn"] = "follows Stunned, which is in the key",
    };

    private static SimState Board()
    {
        var s = new SimState
        {
            Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 30, Hits = new() { 5 }, HitsAfterAllyDies = new() { 6 } } },
            P = { Hp = 40, Orbs = { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } }, AutoHand = new() { new SimHandCard { Id = 3 } },
                  Draw = new SimDrawPile() },
        };
        s.Teammates.Add(new SimPlayer { Hp = 30, Orbs = { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } } });
        return s;
    }

    private static IEnumerable<FieldInfo> FieldsOf(Type t) =>
        t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(f => !f.Name.Contains("k__BackingField"));

    /// <summary>The value changed, or null when this test doesn't know how to change a field of that type.</summary>
    private static object? Changed(object? v, Type t)
    {
        if (t == typeof(int)) return (int)v! == int.MaxValue ? 5 : (int)v! + 7;
        if (t == typeof(long)) return (long)v! + 7;
        if (t == typeof(double)) return (double)v! + 0.5;
        if (t == typeof(bool)) return !(bool)v!;
        if (t == typeof(string)) return (string?)v + "x";
        if (t.IsEnum) return Enum.ToObject(t, Convert.ToInt32(v) + 1);
        if (t == typeof(PowerSet)) return ((PowerSet)v!).With(new PowerId("ProbePower"), 3);
        if (t == typeof(RelicSet)) return ((RelicSet)v!).With(new RelicId("ProbeRelic"), 3);
        if (t == typeof(NamedCounts)) return ((NamedCounts)v!).Add("Probe", 3);
        if (t == typeof(List<int>)) return ((List<int>?)v ?? new()).Append(9).ToList();
        if (t == typeof(List<SimPick>)) return new List<SimPick>((List<SimPick>)v!) { new(4, 0) };
        if (t == typeof(List<TurnEndCard>)) return new List<TurnEndCard>((List<TurnEndCard>)v!) { new("Burn", "Burn", 2) };
        if (t == typeof(List<SimHandCard>)) return new List<SimHandCard> { new() { Id = 5 } };
        if (t == typeof(List<SimOrb>)) return new List<SimOrb> { new() { Kind = OrbKind.Dark, Passive = 6, Evoke = 6 } };
        if (t == typeof(SimDrawPile)) return new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("probe"), 2) } };
        if (t == typeof(List<SimEnemy>) || t == typeof(List<SimPlayer>) || t == typeof(SimPlayer)) return null; // their fields are checked one by one
        return null;
    }

    /// <summary>Every field of the state and of what it holds, as (name, the object holding it in a copy, the field).</summary>
    private static IEnumerable<(string Name, Func<SimState, object> Holder, FieldInfo Field)> AllFields()
    {
        foreach (var f in FieldsOf(typeof(SimState))) yield return ($"SimState.{f.Name}", s => s, f);
        foreach (var f in FieldsOf(typeof(SimPlayer))) yield return ($"SimPlayer.{f.Name}", s => s.P, f);
        foreach (var f in FieldsOf(typeof(SimPlayer))) yield return ($"SimPlayer.{f.Name} (teammate)", s => s.MutableTeammate(0), f);
        foreach (var f in FieldsOf(typeof(SimEnemy))) yield return ($"SimEnemy.{f.Name}", s => s.Enemies[0], f);
        foreach (var f in FieldsOf(typeof(SimOrb))) yield return ($"SimOrb.{f.Name}", s => s.P.OwnOrbs()[0], f);
    }

    [Fact]
    public void EveryFieldIsInTheBranchKeyOrSaysWhyNot()
    {
        var start = Board();
        var key = start.Key();
        var missing = new List<string>();
        var unknown = new List<string>();
        var listedUsed = new HashSet<string>();
        foreach (var (name, holder, field) in AllFields())
        {
            var plain = name.Replace(" (teammate)", "");
            if (NotKeyed.ContainsKey(plain)) { listedUsed.Add(plain); continue; }
            var copy = start.Clone();
            var obj = holder(copy);
            var v = Changed(field.GetValue(obj), field.FieldType);
            if (v == null)
            {
                if (field.FieldType is { } ft && (ft == typeof(List<SimEnemy>) || ft == typeof(List<SimPlayer>) || ft == typeof(SimPlayer))) continue;
                unknown.Add($"{name} ({field.FieldType.Name})");
                continue;
            }
            field.SetValue(obj, v);
            if (copy.Key() == key) missing.Add(name);
        }
        var stale = NotKeyed.Keys.Where(k => !listedUsed.Contains(k)).ToList();
        Assert.True(unknown.Count == 0, "Fields this test can't change (teach Changed their type): " + string.Join(", ", unknown));
        Assert.True(stale.Count == 0, "Listed fields that no longer exist: " + string.Join(", ", stale));
        Assert.True(missing.Count == 0, "Fields the branch key leaves out (add them to SimState.Key, or to NotKeyed with why): " + string.Join(", ", missing));
    }
}

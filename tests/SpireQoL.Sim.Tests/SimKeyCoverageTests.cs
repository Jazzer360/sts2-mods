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
    private const string SetByAMove = "read from the game once; a move its powers' code sets (SetMoveStep: SimEngine.SetMove) changes it with Hits and Hp (its revive), which are in the key";
    private const string Merged = "a per-play result merged on purpose: the guarantee keeps the smallest, the possibility the largest (MergeResults)";
    private const string Scratch = "the engine's scratch inside one step: set and cleared before branches are compared";
    private const string Bookkeeping = "how the search is run, not what happened";

    /// <summary>Fields that may stay out of the key: "Type.Field" and why.</summary>
    private static readonly Dictionary<string, string> NotKeyed = new()
    {
        // The search itself.
        ["SimState.Weight"] = "the branch's share of the rolls: identical branches add theirs when they merge",
        ["SimState.Log"] = Bookkeeping, ["SimState.Work"] = Bookkeeping, ["SimState.ListenerCache"] = Bookkeeping, ["SimState.ListenerSets"] = Bookkeeping, ["SimState.ListenerDead"] = Bookkeeping,
        ["SimState._teammatesShared"] = Bookkeeping, ["SimPlayer._orbsShared"] = Bookkeeping,
        // Decision #313: only a potion's use changes it, on every branch at once as the use starts (RemoveBeforeUse), so no two branches differ in it.
        ["SimPlayer.PotionSlots"] = "the same in every branch: the belt's slots are read from the board and nothing in a fight changes them",
        ["SimState.CardPlaysStarted"] = "a card play's identity within the run (SimCardPlay.Serial): what a model binds is keyed by it, and every bound entry is scratch",
        // Read from the game once and never changed.
        ["SimState.Vars"] = Constant, ["SimState.Fractions"] = Constant, ["SimState.TurnNumber"] = Constant, ["SimState.RoundNumber"] = Constant, ["SimState.Dread"] = Constant, ["SimState.HeldOpenByTheDead"] = Constant, ["SimState.RemovedByItsHook"] = Scratch, ["SimState.KillsNotYetRun"] = Scratch, ["SimState.PerformingMove"] = Scratch, ["SimState.MainRules"] = Constant, ["SimState.History"] = Constant, ["SimState.Titles"] = Constant, ["SimState.TeammatesBeforeMe"] = Constant, ["SimState.CostsUnknown"] = Constant,
        ["SimEnemy.Name"] = Constant, ["SimEnemy.Monster"] = Constant, ["SimEnemy.Hittable"] = Constant, ["SimEnemy.SetMoves"] = Constant,
        ["SimEnemy.StartedAlive"] = Constant, ["SimEnemy.AddedBy"] = "set as a creature a death adds joins, beside its Id (in the key), and never changed",
        ["SimEnemy.Intent"] = SetByAMove, ["SimEnemy.TeammateHits"] = SetByAMove, ["SimEnemy.Move"] = SetByAMove,
        ["SimEnemy.PoisonTriggers"] = Constant, ["SimEnemy.DeathSetsOffFatal"] = Constant,
        ["SimPlayer.SlotOnFirstChannel"] = Constant,
        ["SimPlayer.OstyShields"] = Constant, ["SimPlayer.FairyHeal"] = Constant, ["SimPlayer.BeaconOfHope"] = Constant,
        ["SimPlayer.ExhaustAutoPlays"] = Constant,
        ["SimPlayer.MadeCards"] = Constant, ["SimEnemy.Spawns"] = Constant, ["SimPlayer.Cards"] = Constant,
        // Per-play results, merged.
        ["SimState.PlayHpReached"] = Merged, ["SimState.PlayHpLosses"] = Merged, ["SimState.PlayHpLost"] = Merged,
        ["SimState.PlayBiggestHit"] = Merged, ["SimState.Fired"] = Merged, ["SimState.FiredAny"] = Merged, ["SimState.Flashed"] = Merged, ["SimState.FlashedAny"] = Merged,
        // Inside one step only.
        ["SimState.IndirectNow"] = Scratch, ["SimState.EtherealNow"] = Scratch, ["SimState.CurrentPlay"] = Scratch, ["SimState.AttackSerial"] = Scratch,
        ["SimState.CurrentAttack"] = Scratch, ["SimState.EnemyTurnNow"] = Scratch, ["SimState.SideEnding"] = Scratch, ["SimState.SideStarting"] = Scratch,
        ["SimState.HandLeftOut"] = "the same in every branch of a search: set as a projection's next turn starts, never in a card's simulation (decision #306)",
        ["SimState.EndOfTurnNow"] = Scratch, ["SimState.Bound"] = Scratch, ["SimState.Pending"] = Scratch, ["SimState.PendingIntoDraw"] = Scratch, ["SimState.PendingSpawns"] = Scratch, ["SimState.PendingPowers"] = Scratch, ["SimState.AttackDealt"] = Scratch, ["SimState.ArrowAwayAt"] = Scratch,
        ["SimState.EventCard"] = Scratch, ["SimState.PlayEvents"] = Scratch, ["SimState.PlayCardBlockGains"] = Scratch,
        ["SimState.DrawRecord"] = "in the key while it's set (as a set: decision #120); null between steps",
        ["SimState.OrbTargets"] = "in the key while it's set (an evoke's targets until AfterOrbEvoked); null between steps",
        ["SimState.FirstHits"] = "the damage-formula audit's first hit on each enemy: the outcome keeps it only when every branch has the same",
        // Display logs: the first merged branch's breakdown is shown (Incoming Damage's own run).
        ["SimState.EndOfTurnBlock"] = "Incoming Damage's breakdown: the first merged branch's", ["SimState.Blockable"] = "Incoming Damage's breakdown",
        ["SimState.Unblockable"] = "Incoming Damage's breakdown", ["SimState.EndOfTurnDamage"] = "Incoming Damage's breakdown",
        ["SimState.BlockAtEnemyTurn"] = "your block as the enemy turn starts: Block itself is in the key",
        ["SimEnemy.HpAtTurnEnd"] = "a copy of Hp taken once your end of turn is over (Hp is in the key)",
        ["SimEnemy.UnblockedAtTurnEnd"] = "a copy of UnblockedInstances taken once your end of turn is over (that is in the key)",
        ["SimEnemy.DeathHandled"] = "follows Hp: a dead enemy's death is handled in the same step",
        ["SimEnemy.StunnedThisTurn"] = "follows Stunned, which is in the key",
        ["SimEnemy.Rules"] = "fixed during a run, shared by every branch: the adapter's read of its move rules (decision #307)",
    };

    private static SimState Board()
    {
        var state = new SimState
        {
            Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 30, Hits = new() { 5 } } },
            You = { Hp = 40, Orbs = { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } }, AutoHand = new() { new SimHandCard { Id = 3 } },
                  Draw = new SimDrawPile() },
        };
        state.Teammates.Add(new SimPlayer { Hp = 30, Orbs = { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } } });
        return state;
    }

    private static IEnumerable<FieldInfo> FieldsOf(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(field => !field.Name.Contains("k__BackingField"));

    /// <summary>The value changed, or null when this test doesn't know how to change a field of that type.</summary>
    private static object? Changed(object? v, Type type)
    {
        if (type == typeof(int)) return (int)v! == int.MaxValue ? 5 : (int)v! + 7;
        if (type == typeof(int?)) return v is int value ? value + 7 : 3;
        if (type == typeof(long)) return (long)v! + 7;
        if (type == typeof(double)) return (double)v! + 0.5;
        if (type == typeof(bool)) return !(bool)v!;
        if (type == typeof(string)) return (string?)v + "x";
        if (type.IsEnum) return Enum.ToObject(type, Convert.ToInt32(v) + 1);
        if (type == typeof(PowerSet)) return ((PowerSet)v!).With(new PowerId("ProbePower"), amount: 3);
        if (type == typeof(RelicSet)) return ((RelicSet)v!).With(new RelicId("ProbeRelic"), amount: 3);
        if (type == typeof(NamedCounts)) return ((NamedCounts)v!).Add("Probe", delta: 3);
        if (type == typeof(SimState.CardChanges)) return (SimState.CardChanges)v! with { Upgrades = ((SimState.CardChanges)v!).Upgrades + 1 };
        if (type == typeof(List<int>)) return ((List<int>?)v ?? new()).Append(9).ToList();
        if (type == typeof(int[])) return ((int[]?)v ?? Array.Empty<int>()).Append(9).ToArray();
        if (type == typeof(List<SimPick>)) return new List<SimPick>((List<SimPick>)v!) { new(Card: new HandCardKey("Probe", Digest: 1), Target: 0) };
        if (type == typeof(List<ExhaustingCard>)) return new List<ExhaustingCard>((List<ExhaustingCard>)v!) { new(HandCard: 7, Energy: 2, SimCardType.Skill) };
        if (type == typeof(List<TurnEndCard>)) return new List<TurnEndCard>((List<TurnEndCard>)v!) { new("Burn", "Burn", Amount: 2) };
        if (type == typeof(List<SimHandCard>)) return new List<SimHandCard> { new() { Id = 5, Name = "Probe" } }; // another card (another copy of the same is the same hand)
        if (type == typeof(List<SimOrb>)) return new List<SimOrb> { new() { Kind = OrbKind.Dark, Passive = 6, Evoke = 6 } };
        if (type == typeof(SimPileAutoPlays)) return v is SimPileAutoPlays { ExhaustCards: not null } ? null : new SimPileAutoPlays(new[] { "probe" }) { ExhaustCards = new List<SimDrawCard> { new("probe") } };
        if (type == typeof(SimDrawPile)) return new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("probe"), Count: 2) } };
        if (type == typeof(SimDrawCard)) return new SimDrawCard("probe");
        if (type == typeof(SimStunnedMove)) return new SimStunnedMove(((SimStunnedMove?)v)?.Name + "x", (state, _) => new[] { state });
        if (type == typeof(List<SimEnemy>) || type == typeof(List<SimPlayer>) || type == typeof(SimPlayer)) return null; // their fields are checked one by one
        return null;
    }

    /// <summary>Every field of the state and of what it holds, as (name, the object holding it in a copy, the field).</summary>
    private static IEnumerable<(string Name, Func<SimState, object> Holder, FieldInfo Field)> AllFields()
    {
        foreach (var field in FieldsOf(typeof(SimState))) yield return ($"SimState.{field.Name}", branch => branch, field);
        foreach (var field in FieldsOf(typeof(SimPlayer))) yield return ($"SimPlayer.{field.Name}", branch => branch.You, field);
        foreach (var field in FieldsOf(typeof(SimPlayer))) yield return ($"SimPlayer.{field.Name} (teammate)", branch => branch.MutableTeammate(teammateIndex: 0), field);
        foreach (var field in FieldsOf(typeof(SimEnemy))) yield return ($"SimEnemy.{field.Name}", branch => branch.Enemies[0], field);
        foreach (var field in FieldsOf(typeof(SimOrb))) yield return ($"SimOrb.{field.Name}", branch => branch.You.OwnOrbs()[0], field);
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
            var changed = Changed(field.GetValue(obj), field.FieldType);
            if (changed == null)
            {
                if (field.FieldType is { } type && (type == typeof(List<SimEnemy>) || type == typeof(List<SimPlayer>) || type == typeof(SimPlayer))) continue;
                unknown.Add($"{name} ({field.FieldType.Name})");
                continue;
            }
            field.SetValue(obj, changed);
            if (copy.Key() == key) missing.Add(name);
        }
        var stale = NotKeyed.Keys.Where(otherKey => !listedUsed.Contains(otherKey)).ToList();
        Assert.True(unknown.Count == 0, "Fields this test can't change (teach Changed their type): " + string.Join(", ", unknown));
        Assert.True(stale.Count == 0, "Listed fields that no longer exist: " + string.Join(", ", stale));
        Assert.True(missing.Count == 0, "Fields the branch key leaves out (add them to SimState.Key, or to NotKeyed with why): " + string.Join(", ", missing));
    }
}

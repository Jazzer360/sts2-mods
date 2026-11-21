using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Branches that reach the same state merge, adding their shares of the rolls (SimState.Key). That's what keeps many random hits
/// cheap: the search keeps one branch per distinct state, not one per order of targets. These check that merging never changes an
/// outcome or its chance: the key tells apart every state that can still diverge, and the merged chances equal those counted over
/// every order of targets, one by one.
/// </summary>
public class SimMergeTests
{
    [Fact]
    public void BranchesThatDifferOnlyInATeammateDontMerge()
    {
        var a = new SimState();
        a.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 20 });
        a.Teammates.Add(new SimPlayer { HandCount = 5, Block = 3, Hp = 40 });
        var same = a.Clone();
        Assert.Equal(a.Key(), same.Key());

        void Differs(Action<SimPlayer> change)
        {
            var b = a.Clone();
            change(b.MutableTeammate(0));
            Assert.NotEqual(a.Key(), b.Key());
        }
        Differs(t => t.Block = 4);
        Differs(t => t.Hp = 39);
        Differs(t => t.Taken = 1);
        Differs(t => t.HandCount = 6);
        Differs(t => t.Drawn = 1);
        Differs(t => t.Died = true);
        Differs(t => t.Powers = t.Powers.With(PowerId.Strength, 1));
        Differs(t => t.OwnOrbs().Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }));
    }

    /// <summary>
    /// Random Lightning hits at end of turn (orb passives, front first, each on a random living enemy) on 1 to 4 enemies with
    /// block, small enough that hits kill and later hits retarget. Every order of targets is counted one by one, apart from the
    /// simulator; its chances (each enemy's death, each spread of HP) must be the same.
    /// </summary>
    [Fact]
    public void MergedChancesEqualEveryOrderOfTargetsCounted()
    {
        var r = new Random(4101);
        for (var fight = 0; fight < 300; fight++)
        {
            var enemies = r.Next(1, 5);
            var orbs = r.Next(1, enemies >= 3 ? 6 : 7);
            var hp = Enumerable.Range(0, enemies).Select(_ => r.Next(1, 16)).ToArray();
            var block = Enumerable.Range(0, enemies).Select(_ => r.Next(0, 5)).ToArray();
            var passive = Enumerable.Range(0, orbs).Select(_ => r.Next(1, 7)).ToArray();

            var s = new SimState();
            for (var i = 0; i < enemies; i++) s.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = hp[i], Block = block[i] });
            s.P.OrbCapacity = orbs;
            s.P.Orbs = passive.Select(p => new SimOrb { Kind = OrbKind.Lightning, Passive = p, Evoke = 8 }).ToList();
            var o = SimEngine.Run(s);

            // Every order of targets, one by one: each hit picks a living enemy, all equally likely.
            var counted = new Dictionary<string, double>();
            void Hit(int k, int[] h, int[] b, double weight)
            {
                var alive = Enumerable.Range(0, enemies).Where(i => h[i] > 0).ToList();
                if (k == orbs || alive.Count == 0)
                {
                    var key = string.Join(",", h.Select(x => Math.Max(0, x)));
                    counted[key] = counted.GetValueOrDefault(key) + weight;
                    return;
                }
                foreach (var i in alive)
                {
                    var (h2, b2) = ((int[])h.Clone(), (int[])b.Clone());
                    var absorbed = Math.Min(b2[i], passive[k]);
                    b2[i] -= absorbed;
                    h2[i] -= passive[k] - absorbed;
                    Hit(k + 1, h2, b2, weight / alive.Count);
                }
            }
            Hit(0, hp, block, 1);

            var simulated = o.Branches
                .GroupBy(x => string.Join(",", x.Enemies.OrderBy(e => e.Id).Select(e => Math.Max(0, e.Hp))))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Weight));
            var what = $"fight {fight}: HP [{string.Join(",", hp)}], block [{string.Join(",", block)}], Lightning [{string.Join(",", passive)}]";
            Assert.False(o.Truncated, what);
            Assert.Equal(counted.Keys.OrderBy(k => k), simulated.Keys.OrderBy(k => k));
            foreach (var (key, p) in counted) Assert.True(Math.Abs(p - simulated[key]) < 1e-9, $"{what}: HP {key}: counted {p}, simulated {simulated[key]}");
            for (var i = 0; i < enemies; i++)
            {
                var dead = counted.Where(x => int.Parse(x.Key.Split(',')[i]) == 0).Sum(x => x.Value);
                Assert.True(Math.Abs(dead - o.DeadChance(i)) < 1e-9, $"{what}: enemy {i} dies: counted {dead}, simulated {o.DeadChance(i)}");
            }
        }
    }
}

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
        var state = new SimState();
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 20 });
        state.Teammates.Add(new SimPlayer { HandCount = 5, Block = 3, Hp = 40 });
        var same = state.Clone();
        Assert.Equal(state.Key(), same.Key());

        void Differs(Action<SimPlayer> change)
        {
            var branch = state.Clone();
            change(branch.MutableTeammate(teammateIndex: 0));
            Assert.NotEqual(state.Key(), branch.Key());
        }
        Differs(player => player.Block = 4);
        Differs(player => player.Hp = 39);
        Differs(player => player.Taken = 1);
        Differs(player => player.HandCount = 6);
        Differs(player => player.Drawn = 1);
        Differs(player => player.Died = true);
        Differs(player => player.Powers = player.Powers.With(PowerId.Strength, amount: 1));
        Differs(player => player.OwnOrbs().Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 }));
    }

    /// <summary>
    /// Random Lightning hits at end of turn (orb passives, front first, each on a random living enemy) on 1 to 4 enemies with
    /// block, small enough that hits kill and later hits retarget. Every order of targets is counted one by one, apart from the
    /// simulator; its chances (each enemy's death, each spread of HP) must be the same.
    /// </summary>
    [Fact]
    public void MergedChancesEqualEveryOrderOfTargetsCounted()
    {
        var random = new Random(4101);
        for (var fight = 0; fight < 300; fight++)
        {
            var enemies = random.Next(1, 5);
            var orbs = random.Next(1, enemies >= 3 ? 6 : 7);
            var hp = Enumerable.Range(0, enemies).Select(_ => random.Next(1, 16)).ToArray();
            var block = Enumerable.Range(0, enemies).Select(_ => random.Next(0, 5)).ToArray();
            var passive = Enumerable.Range(0, orbs).Select(_ => random.Next(1, 7)).ToArray();

            var state = new SimState();
            for (var i = 0; i < enemies; i++) state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = hp[i], Block = block[i] });
            state.You.OrbCapacity = orbs;
            state.You.Orbs = passive.Select(passiveDamage => new SimOrb { Kind = OrbKind.Lightning, Passive = passiveDamage, Evoke = 8 }).ToList();
            var outcome = SimEngine.Run(state);

            // Every order of targets, one by one: each hit picks a living enemy, all equally likely.
            var counted = new Dictionary<string, double>();
            void Hit(int orbIndex, int[] hpLeft, int[] blockLeft, double weight)
            {
                var alive = Enumerable.Range(0, enemies).Where(enemyIndex => hpLeft[enemyIndex] > 0).ToList();
                if (orbIndex == orbs || alive.Count == 0)
                {
                    var key = string.Join(",", hpLeft.Select(enemyHpLeft => Math.Max(0, enemyHpLeft)));
                    counted[key] = counted.GetValueOrDefault(key) + weight;
                    return;
                }
                foreach (var target in alive)
                {
                    var (nextHp, nextBlock) = ((int[])hpLeft.Clone(), (int[])blockLeft.Clone());
                    var absorbed = Math.Min(nextBlock[target], passive[orbIndex]);
                    nextBlock[target] -= absorbed;
                    nextHp[target] -= passive[orbIndex] - absorbed;
                    Hit(orbIndex + 1, nextHp, nextBlock, weight / alive.Count);
                }
            }
            Hit(orbIndex: 0, hp, block, weight: 1);

            var simulated = outcome.Branches
                .GroupBy(branch => string.Join(",", branch.Enemies.OrderBy(enemy => enemy.Id).Select(enemy => Math.Max(0, enemy.Hp))))
                .ToDictionary(sameEnemies => sameEnemies.Key, sameEnemies => sameEnemies.Sum(branch => branch.Weight));
            var what = $"fight {fight}: HP [{string.Join(",", hp)}], block [{string.Join(",", block)}], Lightning [{string.Join(",", passive)}]";
            Assert.False(outcome.Truncated, what);
            Assert.Equal(counted.Keys.OrderBy(key => key), simulated.Keys.OrderBy(key => key));
            foreach (var (key, p) in counted) Assert.True(Math.Abs(p - simulated[key]) < 1e-9, $"{what}: HP {key}: counted {p}, simulated {simulated[key]}");
            for (var i = 0; i < enemies; i++)
            {
                var dead = counted.Where(entry => int.Parse(entry.Key.Split(',')[i]) == 0).Sum(entry => entry.Value);
                Assert.True(Math.Abs(dead - outcome.DeadChance(i)) < 1e-9, $"{what}: enemy {i} dies: counted {dead}, simulated {outcome.DeadChance(i)}");
            }
        }
    }
}

using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card's search keeps no breakdown (SimState.Log off: what each source deals and blocks, copied with every branch), but every search
/// keeps the totals as plain numbers: the damage landing on you before block (SimPlayer.Incoming), your block as the enemy turn starts
/// (SimState.BlockAtEnemyTurn) and what no block stops (UnblockableTaken). Play Preview's displays read those from the held card's own
/// result, so they must be what the breakdown adds up to, branch by branch, on the golden's 600 fights.
/// </summary>
public class UnloggedTotalsTests
{
    [Fact]
    public void TheTotalsEveryRunKeepsAreWhatTheBreakdownAddsUpTo()
    {
        var checkedBranches = 0;
        for (var scenario = 0; scenario < 600; scenario++)
        {
            var (start, play) = SinglePlayerGoldenTests.Scenario(scenario);
            var logged = start.Clone();
            logged.Log = true;
            var withBreakdown = SimEngine.Run(logged, play);
            var without = SimEngine.Run(start.Clone(), play);
            Assert.Equal(withBreakdown.Branches.Count, without.Branches.Count);
            for (var i = 0; i < without.Branches.Count; i++)
            {
                var full = withBreakdown.Branches[i];
                var bare = without.Branches[i];
                Assert.True(full.Blockable.Sum(source => source.Amount) == full.You.Incoming, $"scenario {scenario}: blockable {full.Blockable.Sum(source => source.Amount)}, Incoming {full.You.Incoming}");
                Assert.Equal(full.You.Incoming, bare.You.Incoming);
                Assert.Equal(full.BlockAtEnemyTurn, bare.BlockAtEnemyTurn);
                Assert.Equal(full.You.UnblockableTaken, bare.You.UnblockableTaken);
                checkedBranches++;
            }
        }
        Assert.True(checkedBranches > 600);
    }
}

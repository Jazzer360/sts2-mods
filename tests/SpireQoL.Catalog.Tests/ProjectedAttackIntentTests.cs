using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The Intent Forecast's projected attack intent (decision #304) is the game's intent it stands for with the projected hit: its kind and repeats, and
/// the sword the game draws for that damage (AttackIntent.GetAnimation and GetTexture, from GetTotalDamage: under 5, 10, 20, 40), the same number the
/// label and the hover tip are given.
/// </summary>
public class ProjectedAttackIntentTests
{
    private static readonly IEnumerable<Creature> NoTargets = Array.Empty<Creature>();

    [Fact]
    public void ASingleAttackDrawsItsProjectedHit()
    {
        var projected = new ProjectedAttackIntent(new SingleAttackIntent(damage: 6), damage: 12);
        Assert.Equal(IntentType.Attack, projected.IntentType);
        Assert.Equal(1, projected.Repeats);
        Assert.Equal(12, projected.GetTotalDamage(NoTargets, owner: null!));
        Assert.Equal("attack_3", projected.GetAnimation(NoTargets, owner: null!)); // 12: the third sword, as the game's own 12 would be
    }

    [Fact]
    public void AMultiAttackDrawsEachRepeatsProjectedHit()
    {
        var projected = new ProjectedAttackIntent(new MultiAttackIntent(damage: 5, repeat: 3), damage: 7);
        Assert.Equal(3, projected.Repeats);
        Assert.Equal(21, projected.GetTotalDamage(NoTargets, owner: null!));
        Assert.Equal("attack_4", projected.GetAnimation(NoTargets, owner: null!));
    }
}

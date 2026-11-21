using System.Reflection;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Intent Details: the block a move gives its own enemy, read from the move's code (MovePowers.ReadBlock: CreatureCmd.GainBlock on
/// base.Creature, its amount a constant or a number the monster keeps). Block it gives another creature isn't its own.
/// </summary>
public class MoveBlockTests
{
    private static List<MovePowers.BlockCall> Of(string monster, string method)
    {
        GameModels.EnsureLoaded();
        var type = typeof(MegaCrit.Sts2.Core.Models.MonsterModel).Assembly.GetTypes().Single(otherType => otherType.Name == monster && otherType.Namespace == "MegaCrit.Sts2.Core.Models.Monsters");
        var otherMethod = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        return MovePowers.ReadBlock(otherMethod);
    }

    [Fact]
    public void AConstant() => Assert.Equal(18m, Assert.Single(Of("Crusher", "GuardedStrikeMove")).Constant);

    [Fact]
    public void ANumberTheMonsterKeeps() => Assert.Equal("get_EbbBlock", Assert.Single(Of("Aeonglass", "EbbMove")).Getter?.Name);

    [Fact]
    public void BlockGivenToAnotherCreatureIsntItsOwn() => Assert.Empty(Of("Guardbot", "GuardMove"));

    [Fact]
    public void AMoveWithoutBlock() => Assert.Empty(Of("Aeonglass", "EyeLasersMove"));
}

using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Procs.Programs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The program reader reads x ?? y (dup; brtrue past y) as a value of two ways: x when it isn't null, else y (decision #195, round four). It read
/// y alone before, so Vulnerable's dealer.GetPower&lt;CrueltyPower&gt;() ?? dealer.PetOwner?.Creature.GetPower&lt;CrueltyPower&gt;() lost the dealer's
/// own Cruelty. A cached lambda (ldsfld &lt;&gt;9__x; dup; brtrue) is the same lambda either way and reads as before.
/// </summary>
public class CoalesceReadTests
{
    [Fact]
    public void BothSidesOfACoalesceAreWays()
    {
        var method = typeof(VulnerablePower).GetMethod(nameof(VulnerablePower.ModifyDamageMultiplicative))!;
        var text = ProgramReader.ReadReturnedWays(typeof(VulnerablePower), method, RuntimeCode.Read).ToString();
        // The receiver is a value of ways, each way's value after its test (": "): the dealer's own Cruelty, and its player's for a pet.
        Assert.Contains(": dealer.GetPower<CrueltyPower>()", text, StringComparison.Ordinal);
        Assert.Contains(": dealer.PetOwner.Creature.GetPower<CrueltyPower>()", text, StringComparison.Ordinal);
    }
}

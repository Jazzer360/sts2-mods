using SpireQoL.Procs.Programs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What a card does, read from its program (CardEffects, decision #182), on both game branches: each rule finds the cards it was
/// written from, so a change in how the game's code reads shows up here. A new card the rule finds is simulated the same way,
/// without a list.
/// </summary>
public class CardEffectsTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EachRuleFindsTheCardsItWasWrittenFrom(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return;
        using var code = new BranchCode(path);
        var programs = code.Cards().ToDictionary(type => type.Name, type => CardProgramReader.Read(type, code.Read));
        void Finds(Func<CardProgram, bool> rule, params string[] cards)
        {
            foreach (var card in cards)
                if (programs.TryGetValue(card, out var program)) Assert.True(rule(program), $"{card} ({branch}): {program}");
            // A card the rule shouldn't find.
            Assert.False(rule(programs["StrikeIronclad"]));
        }
        Finds(CardEffects.KillsOsty, "Sacrifice", "BoneShards");
        Finds(CardEffects.SetsTargetsHp, "Judgement");
        Finds(CardEffects.TakesTargetsBlock, "Expose");
        Finds(CardEffects.DamagesTargetWithoutAttacking, "CaptureSpirit");
        Finds(CardEffects.BlocksAnother, "Lift", "Constellation", "DemonicShield");
        Finds(CardEffects.BlocksEachPlayer, "Rally");
        Finds(CardEffects.ChannelsForTarget, "Ignition");
        Finds(CardEffects.HitsAllOnKill, "Timber");
        // Your own block is yours: Iron Wave blocks you, not another.
        Assert.False(CardEffects.BlocksAnother(programs["IronWave"]));
    }
}

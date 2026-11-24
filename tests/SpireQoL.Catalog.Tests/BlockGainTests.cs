using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Procs.Programs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Separate times a play gains you block, read from its program (decision #182: BoardCounts.BlockGainCount over CardEffects.BlockGains):
/// each gain on its condition, times its loops' rounds on the board.
/// </summary>
[Collection(GameFightCollection.Name)]
public class BlockGainTests
{
    [Fact]
    public void LoopsGiveTheirRounds()
    {
        var gameFight = new GameFight();
        Assert.Equal(2, BoardCounts.BlockGainCount(gameFight.Add<ChoLimbs>()));
        // Evil Eye: twice after an exhaust this turn (none yet).
        Assert.Equal(1, BoardCounts.BlockGainCount(gameFight.Add<EvilEye>()));
        // Rally: each living player's block in one loop, yours once.
        Assert.Equal(1, BoardCounts.BlockGainCount(gameFight.Add<Rally>()));
    }

    [Fact]
    public void SecondWindGainsOncePerCardItExhausts()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<SecondWind>();
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(0, BoardCounts.BlockGainCount(card));
        gameFight.Add<DefendIronclad>();
        gameFight.Add<Dazed>();
        Assert.Equal(2, BoardCounts.BlockGainCount(card));
    }

    [Fact]
    public void BoneShardsAndSacrificeGainNoBlockWithoutOsty()
    {
        // BoneShards.OnPlay, Sacrifice.OnPlay: nothing happens while Osty is missing (Osty.CheckMissingWithAnim); the game's GainsBlock
        // doesn't say so (the old count predicted their block).
        var gameFight = new GameFight();
        Assert.True(gameFight.Me.IsOstyMissing);
        Assert.Equal(0, BoardCounts.BlockGainCount(gameFight.Add<BoneShards>()));
        Assert.Equal(0, BoardCounts.BlockGainCount(gameFight.Add<Sacrifice>()));
    }

    [Theory]
    [MemberData(nameof(CardEffectsTests.Branches), MemberType = typeof(CardEffectsTests))]
    public void RepeatedGainsAreTheCardsTheTableListed(string branch)
    {
        // PlayFacts.RepeatedBlockCards, as it was when it was deleted: the cards that can gain block more than once in a play.
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return;
        using var code = new BranchCode(path);
        var table = new[] { "ChoFirm", "ChoLimbs", "DeathsDoor", "EvilEye", "Rally", "SecondWind" };
        var found = code.Cards().Where(type => CardEffects.BlockGains(CardProgramReader.Read(type, code.Read)) is var gains
                                                && (gains.Count > 1 || gains.Any(gain => gain.Loops.Count > 0)))
            .Select(type => type.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(table.Where(card => code.Cards().Any(type => type.Name == card)), found);
    }
}

using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Buffer read from its code (decision #230): its GetScaledAmountForMultiplayer never runs (its ShouldScaleInMultiplayer is false), so it reads
/// whole; each branch's own Brightest Flame case is its code's (beta lets HP lost to Brightest Flame through, main stops it), where a main rule did.
/// </summary>
public class ReadBufferTests
{
    [Fact]
    public void BufferStopsHpLossAndTakesAStack()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.Hp = 50;
        state.You.Buffer = 2;
        var hit = Assert.Single(SimEngine.Run(state, new SimPlay
        {
            Name = "Bloodletting", HandCard = 7, HandDelta = -1,
            Card = new SimCardPlay(new SimCard("Bloodletting", SimCardType.Skill), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
            Body = { new LoseHpAction(Amount: 3) },
        }).Branches);
        Assert.Equal(0, hit.You.Taken);
        Assert.Equal(1, hit.You.Buffer);
        // The installed game's Brightest Flame case, as its code reads (beta: BufferPower.ModifyHpLostAfterOstyLate returns the amount for it).
        var buffer = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Buffer.Name));
        var asksFlame = buffer.Spec.Values().OfType<HookFactValue>().Any(fact => fact is { Fact: HookFact.SourceCardIs, Of: "BrightestFlame" });
        var flame = new DamageCtx(Who.You, Target: Who.You, SimProps.Unblockable | SimProps.Unpowered | SimProps.Move, new SimCard("BrightestFlame", SimCardType.Skill), Play: null);
        Assert.Equal(asksFlame ? 3m : 0m, buffer.ModifyHpLostAfterOstyLate(state, Who.You, flame, amount: 3m));
    }

    [Fact]
    public void AReportWithTheRemovedMainFlagStillReads()
    {
        // Main's Buffer and Juggling flags went with their rules (each branch's code is read): a state written with them reads its other flags, by
        // name or number.
        var named = SimState.FromJson("""{"MainRules":"BufferStopsBrightestFlame, JugglingCopiesBeforeThePlay, SplashIsUnpowered"}""");
        Assert.Equal(MainRule.SplashIsUnpowered, named.MainRules);
        Assert.Equal(MainRule.SplashIsUnpowered, SimState.FromJson("""{"MainRules":7}""").MainRules);
        Assert.Equal(MainRule.SplashIsUnpowered, SimState.FromJson(named.ToJson()).MainRules);
    }
}

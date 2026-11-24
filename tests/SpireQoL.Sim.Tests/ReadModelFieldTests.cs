using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A read model's own plain fields (decision #195, round five: a flag or a count of its class, `_triggeredThisTurn`): simulator state its
/// programs read and store (SimPlayer.Fields), seeded from the game, where its hand-written model kept a flag in its relic entry. Each read
/// model here is held to its frozen hand-written model on the path the generic parity boards can't reach.
/// </summary>
public class ReadModelFieldTests
{
    private static SimCardPlay Melody(SimCardTag tag) =>
        new(new SimCard("Played", SimCardType.Skill) { Tags = new() { tag } }, Owner: Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);

    private static SimState Board(SimModel model, bool triggered)
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, MaxHp = 70 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 30 });
        state.You.Relics = state.You.Relics.With(RelicId.ConductorsBaton, amount: 0, data: triggered && model is not ReadModel ? (int)RelicUse.UsedThisTurn : 0);
        if (model is ReadModel read)
        {
            state.You.Fields = state.You.Fields.With(read.FieldKey("_triggeredThisTurn"), triggered ? 1 : 0);
            var questions = read.Spec.Values().OfType<HookHistory>().Select(history => history.Query).Distinct().ToList();
            if (questions.Count > 0) state.History = questions;
        }
        return state;
    }

    /// <summary>Wah, Cho, Ki, then Wah again, each play counted as the engine counts it; whether Conductor's Baton applied its power after each.</summary>
    private static string Run(SimModel model, bool triggered)
    {
        var state = Board(model, triggered);
        var text = new List<string>();
        foreach (var tag in new[] { SimCardTag.Wah, SimCardTag.Cho, SimCardTag.Ki, SimCardTag.Wah })
        {
            var play = Melody(tag);
            state.CurrentPlay = play;
            state.You.Turn = SimKeys.CountPlay(SimKeys.CountPlay(state.You.Turn, PlayStage.Started, play.Card.Type, play.Card.Tags), PlayStage.Finished, play.Card.Type, play.Card.Tags);
            state = Assert.Single(model.AfterCardPlayed(state, Who.You, play));
            text.Add($"{tag}: baton {state.You.Powers[PowerId.ConductorsBaton]}, fired {state.Fired.Has(model.Name)}");
        }
        return string.Join("; ", text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConductorsBatonAppliesItsPowerOnTheThirdMelodyOnceATurn(bool triggered)
    {
        var read = Assert.IsType<ReadModel>(SimModels.Get(RelicId.ConductorsBaton.Name));
        var hand = new HandWritten.ConductorsBaton();
        var run = Run(read, triggered);
        Assert.Equal(Run(hand, triggered), run);
        if (triggered) Assert.DoesNotContain("baton 1", run, StringComparison.Ordinal);
        else Assert.Equal("Wah: baton 0, fired False; Cho: baton 0, fired False; Ki: baton 1, fired True; Wah: baton 1, fired True", run);
    }
}

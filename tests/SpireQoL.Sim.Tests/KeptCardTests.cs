using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card a model keeps is the card play it was kept from, by its identity (decision #272), read from its code: Pen Nib's AttackToDouble (the 10th
/// Attack, kept as it's played: `cardSource == AttackToDouble` doubles its damage, its own AfterCardPlayed lets it go) and Vambrace's TriggeringCard (the
/// card whose block it doubled, kept from the block's cardSource; that card's own AfterCardPlayed sets BlockGainedThisCombat). A card played inside the
/// kept one (Hellraiser's Strike, drawn by it) is another card play: its damage or block isn't the kept card's, and its AfterCardPlayed neither lets
/// the kept card go nor ends Vambrace's bonus.
/// </summary>
public class KeptCardTests
{
    /// <summary>A Strike in the draw pile that plays itself when drawn (Hellraiser), at enemy 1, for 6.</summary>
    private static readonly SimDrawCard HellStrike = new("strike")
    {
        Type = SimCardType.Attack, Strike = true, Auto = new SimHandCard
        {
            Id = 31, Name = "StrikeIronclad", IsAttack = true, Plays =
            {
                new SimPlay
                {
                    Name = "StrikeIronclad", IsAttack = true, AutoPlay = true, HandCard = 31, HandDelta = -1, TargetId = 1,
                    Card = new SimCardPlay(new SimCard("StrikeIronclad", SimCardType.Attack), Who.You, Target: 1, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
                    Body = { Hit(damage: 6) },
                },
            },
        },
    };

    /// <summary>A hit at the play's target through the damage pipeline (Pen Nib's term on it).</summary>
    private static AttackAction Hit(int damage) => ScriptBuilder.Attack(baseDamage: damage);

    private static SimState Board()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 80 } } };
        state.You.Hp = 50;
        state.You.HandCount = 3;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(HellStrike, 3) } };
        state.You.Powers = state.You.Powers.With(PowerId.Hellraiser, amount: 1, data: 0);
        return state;
    }

    private static SimPlay Played(string name, SimCardType type, params SimAction[] body)
    {
        var card = new SimCard(name, type) { GainsBlock = type == SimCardType.Skill };
        var play = new SimPlay { Name = name, IsAttack = type == SimCardType.Attack, HandCard = 7, HandDelta = -1, TargetId = 1,
            Card = new SimCardPlay(card, Who.You, type == SimCardType.Attack ? 1 : Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        play.Body.AddRange(body);
        return play;
    }

    /// <summary>Pen Nib with 9 attacks played: the next Attack is the 10th.</summary>
    private static SimState WithPenNibReady(SimState state)
    {
        state.You.Relics = state.You.Relics.With(RelicId.PenNib, amount: 9);
        SimEngine.SetField(state, Who.You, "PenNib.AttacksPlayed", 9);
        return state;
    }

    [Fact]
    public void PenNibDoublesTheTenthAttacksHitsAroundAStrikePlayedInsideIt()
    {
        // The 10th Attack hits for 5, draws the Strike (an Attack: Pen Nib counts it, 1), which plays inside it for 6, then hits for 5 again: the Strike
        // isn't the kept card (6), and its AfterCardPlayed doesn't let the 10th go, so both of its hits are doubled: 10 + 6 + 10.
        var state = WithPenNibReady(Board());
        SimEngine.ApplyPlayTo(state, Played("Bash", SimCardType.Attack, Hit(damage: 5), new CardEventAction(CardEvent.Drawn, Count: 1), Hit(damage: 5)), out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(1, branch.AutoPlayed);
        Assert.Equal(80 - 26, branch.Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void PenNibOnATeammateDoublesTheirOstysHit()
    {
        // `dealer != base.Owner.Creature && dealer != base.Owner.Osty`: a teammate's Pen Nib doubles their own Osty's powered hit from a card, its 10th
        // Attack not being played (the hit's card outside the play pile).
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 80 } } };
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60 });
        state.MutablePlayer(Who.Teammate(0)).Relics = RelicSet.Empty.With(RelicId.PenNib, amount: 9);
        SimEngine.SetField(state, Who.Teammate(0), "PenNib.AttacksPlayed", 9);
        var model = SimModels.Get(RelicId.PenNib.Name)!;
        var ostysHit = new DamageCtx(Who.PetOf(Who.Teammate(0), PetKind.Osty), Target: 1, SimProps.Move, new SimCard("Flatten", SimCardType.Attack), Play: null);
        Assert.Equal(2m, model.ModifyDamageMultiplicative(state, Who.Teammate(0), ostysHit));
    }

    [Fact]
    public void VambraceDoublesItsCardsBlockAroundAStrikePlayedInsideIt()
    {
        // Vambrace doubles the Defend's first block and keeps it; the Strike played inside it isn't it, so its AfterCardPlayed doesn't end the bonus, and
        // the Defend's second block is doubled too: 10 + 10. Its own AfterCardPlayed then ends it: the next card's block isn't.
        var state = Board();
        state.You.Relics = state.You.Relics.With(RelicId.Vambrace, amount: 0, data: 0);
        SimEngine.ApplyPlayTo(state, Played("Defend", SimCardType.Skill, ScriptBuilder.Block(baseBlock: 5), new CardEventAction(CardEvent.Drawn, Count: 1), ScriptBuilder.Block(baseBlock: 5)), out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(1, branch.AutoPlayed);
        Assert.Equal(20, branch.You.Block);
        SimEngine.ApplyPlayTo(branch, Played("Defend", SimCardType.Skill, ScriptBuilder.Block(baseBlock: 5)), out var next, out _);
        Assert.Equal(25, Assert.Single(next).You.Block);
    }
}

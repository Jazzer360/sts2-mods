using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What a card's play does, end to end: the card built from the game's own objects (GameFight), read by the adapter and played by
/// the simulator, its outcome checked (decision #182, docs/CARD_PROGRAMS.md). They test what happens, not how the code is built, so
/// they hold through a rewrite of either. A bug in a card's play gets a case here as well as its simulator test.
/// </summary>
[Collection(GameFightCollection.Name)]
public class PlayBehaviourTests
{
    private const int StartHp = 80;

    // ---- the fight

    private static Creature Enemy(GameFight gameFight, int hp = 200)
    {
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(hp);
        enemy.SetCurrentHpInternal(hp);
        return enemy;
    }

    private static GameFight Fight()
    {
        var gameFight = new GameFight();
        gameFight.Me.Creature.SetMaxHpInternal(StartHp);
        gameFight.Me.Creature.SetCurrentHpInternal(StartHp);
        gameFight.Me.PlayerCombatState!.Energy = 3;
        return gameFight;
    }

    private static T Power<T>(Creature creature, int amount) where T : PowerModel
    {
        var power = (T)ModelDb.Power<T>().ToMutable();
        power.ApplyInternal(creature, amount);
        return power;
    }

    private static T Relic<T>(GameFight gameFight) where T : RelicModel
    {
        var relic = (T)ModelDb.Relic<T>().ToMutable();
        gameFight.Me.AddRelicInternal(relic);
        return relic;
    }

    private static void Cards<T>(GameFight gameFight, int count, PileType pile) where T : CardModel
    {
        for (var i = 0; i < count; i++) gameFight.Add<T>(pile);
    }

    /// <summary>The card played at <paramref name="target"/> (none: untargeted), every branch right after it resolves.</summary>
    private static List<SimState> PlayOut(GameFight gameFight, CardModel card, Creature? target = null, int plays = 1)
    {
        var start = SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        var facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays);
        Assert.NotNull(facts);
        var play = SimAdapter.Play(card, facts!, gameFight.Me, gameFight.Combat, ids, target == null ? Who.None : ids[target]);
        SimEngine.ApplyPlayTo(start, play, out var branches, out var truncated);
        Assert.False(truncated);
        Assert.NotEmpty(branches);
        return branches;
    }

    private static SimState Only(List<SimState> branches) => Assert.Single(branches);

    // ---- order: the card's code decides it

    [Fact]
    public void IronWaveBlocksBeforeItAttacksSoThornsHitsTheBlock()
    {
        // IronWave.OnPlay: GainBlock, then Attack. Thorns (BeforeDamageReceived) answers the attack with unpowered damage, which the
        // block just gained takes. The crossed Thorns tab shows either way: the attack sets it off and uses up block.
        var gameFight = Fight();
        var enemy = Enemy(gameFight);
        Power<ThornsPower>(enemy, amount: 3);
        var after = Only(PlayOut(gameFight, gameFight.Add<IronWave>(), enemy));
        Assert.Equal(StartHp, after.You.Hp);
        Assert.Equal(5 - 3, after.You.Block);
        Assert.True(after.Fired.Has(nameof(ThornsPower)));
        Assert.Equal(200 - 5, after.Enemies[0].Hp);
    }

    [Fact]
    public void DashBlocksBeforeItAttacks()
    {
        var gameFight = Fight();
        var enemy = Enemy(gameFight);
        Power<ThornsPower>(enemy, amount: 4);
        var after = Only(PlayOut(gameFight, gameFight.Add<Dash>(), enemy));
        Assert.Equal(StartHp, after.You.Hp);
        Assert.Equal(10 - 4, after.You.Block);
    }

    [Fact]
    public void FiendFiresExhaustsSetOffCharonsAshesBeforeItsAttackKills()
    {
        // FiendFire.OnPlay exhausts the hand, then attacks: Charon's Ashes (AfterCardExhausted) hits first, even when the attack
        // would kill (a dev review: the lethal Fiend Fire showed no Charon's Ashes tab).
        var gameFight = Fight();
        var enemy = Enemy(gameFight, hp: 10);
        Relic<CharonsAshes>(gameFight);
        var card = gameFight.Add<FiendFire>();
        Cards<StrikeIronclad>(gameFight, count: 3, PileType.Hand);
        var after = Only(PlayOut(gameFight, card, enemy));
        Assert.True(after.Fired.Has(nameof(CharonsAshes)));
        Assert.True(after.Enemies[0].Hp <= 0);
    }

    [Fact]
    public void FiendFireWithDarkEmbraceDrawsForEachCardItExhaustsAndKeepsThem()
    {
        // Each exhaust draws one (DarkEmbracePower.AfterCardExhausted) into the room the card left; the drawn cards stay (a dev
        // review: 7 drawn where 2 were predicted). Fiend Fire exhausts itself afterwards (its Exhaust keyword), which draws a tenth.
        var gameFight = Fight();
        var enemy = Enemy(gameFight);
        Power<DarkEmbracePower>(gameFight.Me.Creature, amount: 1);
        var card = gameFight.Add<FiendFire>();
        Cards<StrikeIronclad>(gameFight, count: 9, PileType.Hand);
        Cards<DefendIronclad>(gameFight, count: 12, PileType.Draw);
        var after = Only(PlayOut(gameFight, card, enemy));
        Assert.Equal(10, after.PlayEvents[CardEvent.Drawn]);
        Assert.Equal(10, after.You.HandCount);
    }

    [Fact]
    public void BattleTranceDrawsBeforeItsNoDraw()
    {
        // BattleTrance.OnPlay: Draw, then apply No Draw: its own draws happen.
        var gameFight = Fight();
        var card = gameFight.Add<BattleTrance>();
        Cards<DefendIronclad>(gameFight, count: 5, PileType.Draw);
        var after = Only(PlayOut(gameFight, card));
        Assert.Equal(3, after.PlayEvents[CardEvent.Drawn]);
        Assert.Equal(3, after.You.HandCount);
    }

    // ---- amounts and conditions

    [Fact]
    public void ViciousDrawsItsAmountForEachVulnerableApplied()
    {
        var gameFight = Fight();
        var enemy = Enemy(gameFight);
        Power<ViciousPower>(gameFight.Me.Creature, amount: 2);
        var card = gameFight.Add<Bash>();
        Cards<DefendIronclad>(gameFight, count: 5, PileType.Draw);
        var after = Only(PlayOut(gameFight, card, enemy));
        Assert.Equal(2, after.PlayEvents[CardEvent.Drawn]);
    }

    [Fact]
    public void SunderGivesItsEnergyOnlyWhenItKills()
    {
        // Sunder.OnPlay: Attack, then GainEnergy only if a hit killed its target.
        var killed = Fight();
        var weak = Enemy(killed, hp: 5);
        var killedAfter = Only(PlayOut(killed, killed.Add<Sunder>(), weak));
        var survived = Fight();
        var strong = Enemy(survived, hp: 200);
        var survivedAfter = Only(PlayOut(survived, survived.Add<Sunder>(), strong));
        Assert.Equal(survivedAfter.You.Energy + 3, killedAfter.You.Energy);
    }

    [Fact]
    public void WhirlwindHitsOncePerEnergy()
    {
        var gameFight = Fight();
        var enemy = Enemy(gameFight);
        var after = Only(PlayOut(gameFight, gameFight.Add<Whirlwind>(), enemy));
        Assert.Equal(200 - 3 * 5, after.Enemies[0].Hp);
    }

    [Fact]
    public void SecondWindExhaustsEachCardThenGainsItsBlock()
    {
        // SecondWind.OnPlay: for each non-Attack in hand, Exhaust it, then GainBlock. Feel No Pain gains block on each exhaust.
        var gameFight = Fight();
        Enemy(gameFight);
        Power<FeelNoPainPower>(gameFight.Me.Creature, amount: 3);
        var card = gameFight.Add<SecondWind>();
        Cards<DefendIronclad>(gameFight, count: 2, PileType.Hand);
        gameFight.Add<StrikeIronclad>();
        var after = Only(PlayOut(gameFight, card));
        Assert.Equal(2 * 5 + 2 * 3, after.You.Block);
        Assert.Equal(1, after.You.HandCount);
    }

    // ---- a pick in the middle of a play is a card, never a particular one (decision #92)

    [Fact]
    public void AcrobaticsWithASlyCardInHandPredictsItsDrawAndOneDiscardAndNothingAboutTheSlyCard()
    {
        // Acrobatics draws, then you pick a card to discard. Which one is your own decision: a Sly card playing itself when it's the one
        // isn't simulated, and nothing is marked unknown. The draw and the discard (with what a discard sets off) are exact.
        var gameFight = Fight();
        Enemy(gameFight);
        var card = gameFight.Add<Acrobatics>();
        gameFight.Add<Reflex>();
        Cards<DefendIronclad>(gameFight, count: 5, PileType.Draw);
        var facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1);
        Assert.False(facts.MayDiscardSly);
        var after = Only(PlayOut(gameFight, card));
        Assert.Null(after.Unmodelled);
        Assert.Equal(3, after.PlayEvents[CardEvent.Drawn]);
        Assert.Equal(1, after.PlayEvents[CardEvent.Discarded]);
        Assert.Equal(1 + 3 - 1, after.You.HandCount);
    }

    [Fact]
    public void StormOfSteelDiscardingASlyCardWithoutAChoiceIsStillNotModelled()
    {
        // The whole hand goes, no pick: the Sly card certainly plays itself, which the simulator doesn't follow yet (tracker).
        var gameFight = Fight();
        Enemy(gameFight);
        var card = gameFight.Add<StormOfSteel>();
        gameFight.Add<Reflex>();
        Assert.True(PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1).MayDiscardSly);
    }

    // ---- the cards' own hooks: they listen from your piles, after your powers and relics

    [Fact]
    public void AVoidItDrawsTakesItsEnergy()
    {
        // Shrug It Off draws the Void, whose own AfterCardDrawn takes 1 energy: the adapter reads it among the cards that listen from
        // your piles (SimPlayer.Cards), with its energy in its draw profile.
        int EnergyAfterDrawing<T>() where T : CardModel
        {
            var gameFight = Fight();
            Enemy(gameFight);
            var card = gameFight.Add<ShrugItOff>();
            gameFight.Add<T>(PileType.Draw);
            return Only(PlayOut(gameFight, card)).You.Energy;
        }
        Assert.Equal(EnergyAfterDrawing<DefendIronclad>() - 1, EnergyAfterDrawing<MegaCrit.Sts2.Core.Models.Cards.Void>());
    }

    // ---- what happens between plays

    [Fact]
    public void KunaisDexterityLandsBetweenTwoPlaysOfIronWave()
    {
        // Kunai counts the first play (AfterCardPlayed) and gives Dexterity, which the second play's block gets.
        var gameFight = Fight();
        var enemy = Enemy(gameFight);
        var kunai = Relic<Kunai>(gameFight);
        AccessTools.Field(typeof(Kunai), "_attacksPlayedThisTurn").SetValue(kunai, 2);
        var after = Only(PlayOut(gameFight, gameFight.Add<IronWave>(), enemy, plays: 2));
        Assert.Equal(5 + 6, after.You.Block);
        Assert.Equal(1, after.You.Powers[PowerId.Dexterity]);
    }

    [Fact]
    public void TuningForkFiresOnACardWhosePickIsYours()
    {
        // A choice mid-play decides which card, not that the play goes on: Tuning Fork at 9 counts Acrobatics and fires its block.
        var gameFight = Fight();
        Enemy(gameFight);
        var fork = Relic<TuningFork>(gameFight);
        AccessTools.Field(typeof(TuningFork), "_skillsPlayed").SetValue(fork, 9);
        var card = gameFight.Add<Acrobatics>();
        gameFight.Add<StrikeIronclad>();
        Cards<DefendIronclad>(gameFight, count: 5, PileType.Draw);
        var branches = PlayOut(gameFight, card);
        Assert.All(branches, branch => Assert.True(branch.Fired.Has(nameof(TuningFork))));
        Assert.All(branches, branch => Assert.Equal(7, branch.You.Block)); // its Block value
    }
}

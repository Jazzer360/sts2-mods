using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A power a card's play applies has the amount its code gives it (decision #282): its step's amount read on the board (a card value by the key
/// DynamicVarSet's property looks it up by: DynamicVars.Doom is "DoomPower"), or what the play's attack dealt; an amount that can't be read is
/// an unknown (SimAdapter.PowerAmountUnread), never a guess of 1.
/// </summary>
[Collection(GameFightCollection.Name)]
public class PowerAmountTests
{
    /// <summary>The card's play in a fight with one enemy it's aimed at, upgraded or not.</summary>
    private static SimPlay PlayOf(CardModel canon, bool upgraded)
    {
        GameModels.EnsureLoaded();
        var gameFight = new GameFight();
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.Axebot>().ToMutable(), MegaCrit.Sts2.Core.Combat.CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(200);
        enemy.SetCurrentHpInternal(200);
        var card = gameFight.Combat.CreateCard(canon, gameFight.Me);
        if (upgraded && card.MaxUpgradeLevel >= 1)
        {
            card.UpgradeInternal();
            card.FinalizeUpgradeInternal();
        }
        PileType.Hand.GetPile(gameFight.Me).AddInternal(card);
        SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        return SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, card.TargetType == TargetType.AnyEnemy ? ids[enemy] : Who.None, plays: 1);
    }

    private static CardModel Canon<T>() where T : CardModel
    {
        GameModels.EnsureLoaded();
        return ModelDb.Card<T>();
    }

    private static IEnumerable<SimAction> Flattened(IEnumerable<SimAction> actions) =>
        actions.SelectMany(action => action is WhenAction guarded ? Flattened(guarded.Then).Prepend(action) : new[] { action });

    private static ApplyPowerAction Applied(SimPlay play, string power) =>
        Assert.Single(Flattened(play.Body).OfType<ApplyPowerAction>(), apply => apply.Power.Name == power);

    [Fact]
    public void APowerAmountNamedByADynamicVarSetPropertyIsItsValue()
    {
        // Oblivion.OnPlay: PowerCmd.Apply<OblivionPower>(..., base.DynamicVars.Doom.BaseValue, ...), Doom being _vars["DoomPower"]: 3, upgraded 4.
        Assert.Equal(3, Applied(PlayOf(Canon<Oblivion>(), upgraded: false), "OblivionPower").Amount);
        Assert.Equal(4, Applied(PlayOf(Canon<Oblivion>(), upgraded: true), "OblivionPower").Amount);
        // Rupture.OnPlay: PowerCmd.Apply<RupturePower>(..., base.DynamicVars.Strength.BaseValue, ...): 1, upgraded 2.
        Assert.Equal(1, Applied(PlayOf(Canon<Rupture>(), upgraded: false), "RupturePower").Amount);
        Assert.Equal(2, Applied(PlayOf(Canon<Rupture>(), upgraded: true), "RupturePower").Amount);
    }

    [Fact]
    public void BlightStrikesDoomIsWhatItsAttackDealt()
    {
        // BlightStrike.OnPlay: PowerCmd.Apply<DoomPower>(cardPlay.Target, attackCommand.Results.SelectMany(r => r).Sum(r => r.TotalDamage), ...).
        var doom = Applied(PlayOf(Canon<BlightStrike>(), upgraded: false), "DoomPower");
        Assert.True(doom.FromAttackDealt);
        Assert.Equal(ApplyPowerAction.Where.Target, doom.To);
        Assert.True(doom.Debuff);
    }

    public static TheoryData<string> Branches => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryCardsPowerAmountReadsOnBothBranches(string branch)
    {
        // The card scan's check (SimAdapter.PowerAmountsUnread) on each branch's code: a card value is by the key its DynamicVarSet property looks
        // up (main's own getters), and no amount of a power the simulator acts on is a part the reader couldn't follow or another command's result.
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        GameModels.EnsureLoaded();
        var code = BranchCode.Of(path);
        var unread = new List<string>();
        var oblivion = code.Cards().Single(type => type.Name == nameof(Oblivion));
        Assert.Contains(SpireQoL.Procs.Programs.ProgramReader.Read(oblivion, code.Read).Commands, command => command.Command.Name == "Apply"
            && command.Values.Count > 2 && command.Values[2] is SpireQoL.Procs.Programs.CardValue { Key: "DoomPower" });
        foreach (var type in code.Cards())
        {
            var program = SpireQoL.Procs.Programs.ProgramReader.Read(type, code.Read);
            var game = type.Assembly;
            unread.AddRange(SimAdapter.PowerAmountsUnread(program, key => SpireQoL.Procs.Programs.ProgramReader.DynamicVarKey(game, key, code.Read) == key)
                .Select(each => $"{type.Name}: {each}"));
        }
        Assert.True(unread.Count == 0, string.Join("\n", unread));
    }

    [Fact]
    public void NoCardsPowerAmountIsUnread()
    {
        // Every card's play, upgraded and not: no power it applies has an amount the adapter can't read (it would be an unknown).
        GameModels.EnsureLoaded();
        var unread = new List<string>();
        foreach (var canon in ModelDb.AllCards.OrderBy(card => card.GetType().Name, StringComparer.Ordinal).ToList())
            foreach (var upgraded in new[] { false, true })
            {
                SimPlay play;
                try { play = PlayOf(canon, upgraded); }
                catch (Exception) { continue; } // a card the test fight can't build: the card snapshots hold it
                foreach (var unknown in Flattened(play.Body).OfType<NotModelledAction>().Where(action => action.Reason.StartsWith(SimAdapter.PowerAmountUnread, StringComparison.Ordinal)))
                    unread.Add($"{canon.GetType().Name}{(upgraded ? "+" : "")}: {unknown.Reason}");
            }
        Assert.True(unread.Count == 0, string.Join("\n", unread));
    }
}

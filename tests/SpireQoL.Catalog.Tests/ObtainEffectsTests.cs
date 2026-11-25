using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Data;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The reminders check reads what a relic an event option gave you does as you obtain it (RunReminders.ObtainEffects:
/// its AfterObtained), so that isn't counted against the option's code. A report: This or That's Ornate gave a random relic,
/// Cursed Pearl, whose gold was reported as the option's.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ObtainEffectsTests
{
    [Fact]
    public void RelicsThatGiveGoldWhenObtainedReadAsGold()
    {
        GameModels.EnsureLoaded();
        foreach (var relic in new RelicModel[] { ModelDb.Relic<CursedPearl>(), ModelDb.Relic<GoldenPearl>(), ModelDb.Relic<OldCoin>(), ModelDb.Relic<SignetRing>() })
            Assert.True(RunReminders.ObtainEffects(relic).HasFlag(ChoiceEffect.GoldGain), relic.GetType().Name);
    }

    [Fact]
    public void HeftyTabletsInjuryReadsAsACurse()
    {
        // Report 20261125-010503-1b7d90: HeftyTablet.AfterObtained adds the rare card you pick and RunState.CreateCard<Injury>(owner), a Curse,
        // with one CardPileCmd.Add(list, PileType.Deck): read as a card only, the Injury's CurseToDeck was reported as unread.
        GameModels.EnsureLoaded();
        var effects = RunReminders.ObtainEffects(ModelDb.Relic<HeftyTablet>());
        Assert.True(effects.HasFlag(ChoiceEffect.CardToDeck), effects.ToString());
        Assert.True(effects.HasFlag(ChoiceEffect.CurseToDeck), effects.ToString());
    }

    /// <summary>
    /// Every relic whose AfterObtained code names a curse card's class anywhere (a generic argument of any call, a constructor, a type token: how the
    /// code makes it doesn't matter) reads as adding a curse, so the next relic that makes one some new way is caught here, not by a player's report.
    /// </summary>
    [Fact]
    public void EveryRelicWhoseObtainNamesACurseReadsAsACurse()
    {
        GameModels.EnsureLoaded();
        var naming = new List<string>();
        var missed = new List<string>();
        foreach (var relic in ModelDb.AllRelics)
        {
            var type = relic.GetType();
            if (type.GetMethod(nameof(RelicModel.AfterObtained)) is not { } obtained || obtained.DeclaringType != type) continue;
            if (!NamesACurse(obtained, type)) continue;
            naming.Add(type.Name);
            if (!RunReminders.ObtainEffects(relic).HasFlag(ChoiceEffect.CurseToDeck)) missed.Add(type.Name);
        }
        Assert.Contains(nameof(HeftyTablet), naming);
        Assert.True(missed.Count == 0, "relics whose AfterObtained makes a curse but don't read as CurseToDeck: " + string.Join(", ", missed));
    }

    /// <summary>The method's code (its async state machine's, and the methods of the types nested in its class: lambdas) names a curse card's class.</summary>
    private static bool NamesACurse(System.Reflection.MethodInfo method, Type owner)
    {
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        var bodies = new List<System.Reflection.MethodBase> { method };
        foreach (var nested in owner.GetNestedTypes(all)) bodies.AddRange(nested.GetMethods(all));
        foreach (var body in bodies)
        {
            List<HarmonyLib.CodeInstruction> code;
            try { code = SpireQoL.Diagnostics.GameCode.Instructions(body); }
            catch { continue; }
            foreach (var instruction in code)
            {
                var named = instruction.operand switch
                {
                    System.Reflection.MethodInfo { IsGenericMethod: true } generic => generic.GetGenericArguments(),
                    System.Reflection.ConstructorInfo constructor => new[] { constructor.DeclaringType! },
                    Type token => new[] { token },
                    _ => Array.Empty<Type>(),
                };
                if (named.Any(RunReminders.IsCurseCard)) return true;
            }
        }
        return false;
    }

    [Fact]
    public void ARelicThatDoesNothingWhenObtainedReadsAsNothing()
    {
        GameModels.EnsureLoaded();
        Assert.Equal(ChoiceEffect.None, RunReminders.ObtainEffects(ModelDb.Relic<Anchor>()));
    }
}

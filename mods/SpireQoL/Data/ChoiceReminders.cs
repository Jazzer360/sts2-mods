using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireQoL.Data;

/// <summary>What choosing an event option does, as far as relics care (read from the option's code).</summary>
[Flags]
public enum ChoiceEffect
{
    None = 0,
    /// <summary>A card joins your deck: added, transformed into, or a card reward.</summary>
    CardToDeck = 1,
    /// <summary>A curse joins your deck.</summary>
    CurseToDeck = 2,
    /// <summary>You lose HP (CreatureCmd.Damage).</summary>
    HpLoss = 4,
    /// <summary>You gain gold (directly or as a gold reward).</summary>
    GoldGain = 8,
    /// <summary>You get a potion (directly or as a potion reward).</summary>
    Potion = 16,
    /// <summary>A rest-site heal (events that let you rest: the rest-site relics fire).</summary>
    RestHeal = 32,
}

/// <param name="Relic">The relic's class name.</param>
/// <param name="On">Choices it reacts to: its icon goes next to those event options.</param>
/// <param name="CrossedOn">Choices where it works against you (red slash).</param>
/// <param name="OnCardReward">It has a row in the card reward panel.</param>
public sealed record ReminderRule(string Relic, ChoiceEffect On, ChoiceEffect CrossedOn = ChoiceEffect.None, bool OnCardReward = false);

/// <summary>
/// Out-of-combat reminders (docs/FEATURES.md "Run Reminders"): which relics change what a choice
/// outside combat does. Written from each relic's hook bodies in the decompile; every relic with a
/// choice hook has a rule here or a reviewed reason in Diagnostics/RelicChoiceTracking.cs.
/// </summary>
public static class ChoiceReminders
{
    private const ChoiceEffect AnyCard = ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck;

    public static readonly IReadOnlyList<ReminderRule> Rules = new ReminderRule[]
    {
        // AfterCardChangedPiles into your deck (transforms included, clones not for Bing Bong).
        new(nameof(BingBong), AnyCard, CrossedOn: ChoiceEffect.CurseToDeck, OnCardReward: true),
        new(nameof(BookOfFiveRings), AnyCard, OnCardReward: true),
        new(nameof(LuckyFysh), AnyCard, OnCardReward: true),
        new(nameof(DarkstonePeriapt), ChoiceEffect.CurseToDeck),
        // TryModifyCardRewardAlternatives: the sacrifice button, counting toward a relic.
        new(nameof(PaelsWing), ChoiceEffect.None, OnCardReward: true),
        // TryModifyCardBeingAddedToDeck: cards joining your deck from an event are upgraded or
        // enchanted when they qualify (rewards and shops already show it on the offered cards).
        new(nameof(MoltenEgg), ChoiceEffect.CardToDeck),
        new(nameof(FrozenEgg), ChoiceEffect.CardToDeck),
        new(nameof(ToxicEgg), ChoiceEffect.CardToDeck),
        new(nameof(FresnelLens), ChoiceEffect.CardToDeck),
        // HP loss outside combat (ModifyHpLostAfterOsty; Beating Remnant only caps in combat).
        new(nameof(TungstenRod), ChoiceEffect.HpLoss),
        // Gold (ModifyGoldGained, AfterGoldGained).
        new(nameof(BowlerHat), ChoiceEffect.GoldGain),
        new(nameof(DragonFruit), ChoiceEffect.GoldGain),
        new(nameof(Ectoplasm), ChoiceEffect.GoldGain, CrossedOn: ChoiceEffect.GoldGain),
        // Potions (ShouldProcurePotion; Belt Buckle's Dexterity needs no potions at combat start).
        new(nameof(Sozu), ChoiceEffect.Potion, CrossedOn: ChoiceEffect.Potion),
        new(nameof(BeltBuckle), ChoiceEffect.Potion, CrossedOn: ChoiceEffect.Potion),
        // Rest-site heals, mimicked ones included (none of these check isMimicked).
        new(nameof(RegalPillow), ChoiceEffect.RestHeal),
        new(nameof(StoneHumidifier), ChoiceEffect.RestHeal),
        new(nameof(DreamCatcher), ChoiceEffect.RestHeal),
        new(nameof(TinyMailbox), ChoiceEffect.RestHeal),
    };

    private static readonly Dictionary<string, ReminderRule> ByRelic = Rules.ToDictionary(r => r.Relic);

    public static ReminderRule? For(string relic) => ByRelic.GetValueOrDefault(relic);

    /// <summary>
    /// Game calls ("Type.Method", "Type.Prefix*", constructors as "Type..ctor") and what they mean.
    /// Events only (combat never shows event options).
    /// </summary>
    public static readonly IReadOnlyList<(string Call, ChoiceEffect Effect)> Calls = new[]
    {
        ("CardPileCmd.Add", ChoiceEffect.CardToDeck),
        ("CardCmd.Transform*", ChoiceEffect.CardToDeck),
        ("CardReward..ctor", ChoiceEffect.CardToDeck),
        ("CardPileCmd.AddCurse*", ChoiceEffect.CurseToDeck),
        ("CreatureCmd.Damage", ChoiceEffect.HpLoss),
        ("PlayerCmd.GainGold", ChoiceEffect.GoldGain),
        ("GoldReward..ctor", ChoiceEffect.GoldGain),
        ("PotionCmd.TryToProcure", ChoiceEffect.Potion),
        ("PotionReward..ctor", ChoiceEffect.Potion),
        ("PlayerCmd.MimicRestSiteHeal", ChoiceEffect.RestHeal),
    };

    public static ChoiceEffect EffectsOf(IEnumerable<string> calls)
    {
        var e = ChoiceEffect.None;
        foreach (var c in calls)
        {
            foreach (var (call, effect) in Calls)
            {
                if (call.EndsWith('*') ? c.StartsWith(call[..^1]) : c == call) e |= effect;
            }
        }
        return e;
    }
}

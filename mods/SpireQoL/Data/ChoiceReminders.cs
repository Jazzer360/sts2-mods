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
    /// <summary>You buy something in a shop (AfterItemPurchased). Shops only: no event call maps to it.</summary>
    Purchase = 64,
    /// <summary>A card in your deck is enchanted (CardCmd.Enchant, an enchantment pick from your deck).</summary>
    EnchantCard = 128,
    /// <summary>A card in your deck is upgraded (CardCmd.Upgrade, an upgrade pick from your deck).</summary>
    UpgradeCard = 256,
}

/// <param name="Relic">The relic's class name.</param>
/// <param name="On">Choices it reacts to: its icon goes next to those event options.</param>
/// <param name="CrossedOn">Choices where it works against you (red slash).</param>
/// <param name="OnCardReward">It has a row in the card reward panel.</param>
/// <param name="Expect">Effects that always make it flash when they happen (the check reports an icon
/// whose effect happened without the relic reacting). Relics that react without flashing leave it empty.</param>
/// <param name="ShopIcon">Its icon goes beside shop items it reacts to; false when the shop already shows the effect on the item
/// (the eggs and Fresnel Lens change the offered card itself).</param>
public sealed record ReminderRule(string Relic, ChoiceEffect On, ChoiceEffect CrossedOn = ChoiceEffect.None, bool OnCardReward = false,
    ChoiceEffect Expect = ChoiceEffect.None, bool ShopIcon = true)
{
    /// <summary>Effects a shop item can have: buying it, and a card or a potion joining you.</summary>
    public const ChoiceEffect ShopEffects = ChoiceEffect.Purchase | ChoiceEffect.CardToDeck | ChoiceEffect.Potion;

    /// <summary>Where its reminder shows (each place has its own switch).</summary>
    public bool In(ReminderPlace place) => place switch
    {
        ReminderPlace.Reward => OnCardReward,
        ReminderPlace.Event => (On & ~ChoiceEffect.Purchase) != 0,
        ReminderPlace.Shop => ShopIcon && (On & ShopEffects) != 0,
        _ => false,
    };
}

/// <summary>Where a Run Reminders icon or row shows outside fights.</summary>
public enum ReminderPlace { Reward, Event, Shop }

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
        new(nameof(BingBong), AnyCard, CrossedOn: ChoiceEffect.CurseToDeck, OnCardReward: true, Expect: AnyCard),
        new(nameof(BookOfFiveRings), AnyCard, OnCardReward: true),
        new(nameof(LuckyFysh), AnyCard, OnCardReward: true, Expect: AnyCard),
        new(nameof(DarkstonePeriapt), ChoiceEffect.CurseToDeck, Expect: ChoiceEffect.CurseToDeck),
        // TryModifyCardRewardAlternatives: the sacrifice button, counting toward a relic.
        new(nameof(PaelsWing), ChoiceEffect.None, OnCardReward: true),
        // TryModifyCardBeingAddedToDeck: cards joining your deck from an event are upgraded or
        // enchanted when they qualify (rewards and shops already show it on the offered cards).
        new(nameof(MoltenEgg), ChoiceEffect.CardToDeck, ShopIcon: false),
        new(nameof(FrozenEgg), ChoiceEffect.CardToDeck, ShopIcon: false),
        new(nameof(ToxicEgg), ChoiceEffect.CardToDeck, ShopIcon: false),
        new(nameof(FresnelLens), ChoiceEffect.CardToDeck, ShopIcon: false),
        // Cards that are stronger in fights in a way their text doesn't show (Data/CardAffinity): options that give one, or
        // that make the change they look for (Miniature Cannon: an upgrade; Mystic Lighter: an enchantment, Tezcatara's soup).
        new(nameof(StrikeDummy), ChoiceEffect.CardToDeck, ShopIcon: false),
        new(nameof(FakeStrikeDummy), ChoiceEffect.CardToDeck, ShopIcon: false),
        new(nameof(MiniatureCannon), ChoiceEffect.CardToDeck | ChoiceEffect.UpgradeCard, ShopIcon: false),
        new(nameof(MysticLighter), ChoiceEffect.CardToDeck | ChoiceEffect.EnchantCard, ShopIcon: false),
        new(nameof(VitruvianMinion), ChoiceEffect.CardToDeck, ShopIcon: false),
        // HP loss outside combat (ModifyHpLostAfterOsty; Beating Remnant only caps in combat).
        new(nameof(TungstenRod), ChoiceEffect.HpLoss, Expect: ChoiceEffect.HpLoss),
        // Gold (ModifyGoldGained, AfterGoldGained).
        new(nameof(BowlerHat), ChoiceEffect.GoldGain, Expect: ChoiceEffect.GoldGain),
        new(nameof(DragonFruit), ChoiceEffect.GoldGain, Expect: ChoiceEffect.GoldGain),
        new(nameof(Ectoplasm), ChoiceEffect.GoldGain, CrossedOn: ChoiceEffect.GoldGain),
        // Potions (ShouldProcurePotion; Belt Buckle's Dexterity needs no potions at combat start).
        new(nameof(Sozu), ChoiceEffect.Potion, CrossedOn: ChoiceEffect.Potion),
        new(nameof(BeltBuckle), ChoiceEffect.Potion, CrossedOn: ChoiceEffect.Potion),
        // Shops: buying anything ends Maw Bank's gold per room (AfterItemPurchased sets HasItemBeenBought).
        new(nameof(MawBank), ChoiceEffect.Purchase, CrossedOn: ChoiceEffect.Purchase),
        // Rest-site heals, mimicked ones included (none of these check isMimicked).
        new(nameof(RegalPillow), ChoiceEffect.RestHeal),
        new(nameof(StoneHumidifier), ChoiceEffect.RestHeal),
        new(nameof(DreamCatcher), ChoiceEffect.RestHeal),
        new(nameof(TinyMailbox), ChoiceEffect.RestHeal),
    };

    private static readonly Dictionary<string, ReminderRule> ByRelic = Rules.ToDictionary(r => r.Relic);

    public static ReminderRule? For(string relic) => ByRelic.GetValueOrDefault(relic);

    /// <summary>The settings key for a relic's reminder at one place ("reminder-shop:MawBank").</summary>
    public static string Key(ReminderPlace place, string relic) => place switch
    {
        ReminderPlace.Reward => "reminder-reward:",
        ReminderPlace.Event => "reminder-event:",
        _ => "reminder-shop:",
    } + relic;

    /// <summary>Recorded with the calls when the option makes a curse card (ModelDb.Card of a curse, the curse pool).</summary>
    public const string CurseMade = "(curse card)";

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
        (CurseMade, ChoiceEffect.CurseToDeck),
        ("CreatureCmd.Damage", ChoiceEffect.HpLoss),
        ("PlayerCmd.GainGold", ChoiceEffect.GoldGain),
        ("GoldReward..ctor", ChoiceEffect.GoldGain),
        ("PotionCmd.TryToProcure", ChoiceEffect.Potion),
        ("PotionReward..ctor", ChoiceEffect.Potion),
        ("PlayerCmd.MimicRestSiteHeal", ChoiceEffect.RestHeal),
        ("CardCmd.Enchant*", ChoiceEffect.EnchantCard),
        ("CardSelectCmd.FromDeckForEnchantment", ChoiceEffect.EnchantCard),
        ("CardCmd.Upgrade*", ChoiceEffect.UpgradeCard),
        ("CardSelectCmd.FromDeckForUpgrade", ChoiceEffect.UpgradeCard),
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

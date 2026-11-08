using HarmonyLib;
using Sts2Mods.Core.Features;

namespace SpireQoL.Features;

/// <summary>
/// Sums all blockable damage headed at the player this round: enemy attack intents
/// (AttackIntent.GetTotalDamage) plus blockable end-of-turn self damage (Burn, Decay, Constrict...).
/// Shown next to the player's block and previewed on the health bar, alongside the game's
/// existing Poison/Doom preview in NHealthBar. Spec: docs/FEATURES.md#incoming-damage.
/// Unblockable HP loss (Regret, Poison, Demise) is listed separately, never mixed into the total.
/// </summary>
public sealed class IncomingDamageFeature : IFeature
{
    public string Id => "incoming-damage";
    public string DisplayName => "Incoming Damage";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Not implemented yet: no patches applied.
    }
}

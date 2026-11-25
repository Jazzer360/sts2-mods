using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace SpireQoL.Features;

/// <summary>
/// A forecast turn's attack intent drawn with its projected hit (decision #304: what it hits you for then, the simulator's projection), so the game's
/// own intent node (NIntent) shows it everywhere it reads the intent: the label (GetIntentLabel), the sword's size (GetTexture and GetAnimation, from
/// GetTotalDamage) and the hover tip (GetIntentDescription). Everything else is the game's intent it stands for: its kind, title, sprite and
/// repeats; one whose own class draws itself (Death Blow's fixed sprite) keeps its own looks. Only the forecast's nodes get one: the intents the game
/// shows are untouched.
/// </summary>
internal sealed class ProjectedAttackIntent : AttackIntent
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly AttackIntent _intent;
    private readonly int _damage;

    public ProjectedAttackIntent(AttackIntent intent, int damage)
    {
        _intent = intent;
        _damage = damage;
        DamageCalc = intent.DamageCalc;
    }

    /// <summary>The projected hit, each of its repeats.</summary>
    public int Damage => _damage;

    public override IntentType IntentType => _intent.IntentType;

    public override int Repeats => _intent.Repeats;

    protected override string IntentPrefix => Read<string>(nameof(IntentPrefix)) ?? base.IntentPrefix;

    protected override string SpritePath => Read<string>(nameof(SpritePath)) ?? base.SpritePath;

    /// <summary>One of the game intent's own protected properties (its class's override), read through its getter.</summary>
    private T? Read<T>(string property) where T : class
    {
        try { return _intent.GetType().GetProperty(property, Instance)?.GetValue(_intent) as T; }
        catch (Exception exception) when (exception is AmbiguousMatchException or TargetInvocationException) { return null; } // AttackIntent's own then
    }

    /// <summary>Its class draws itself (a GetTexture of its own, not AttackIntent's sword by damage): its looks are its own.</summary>
    private bool OwnLooks => _intent.GetType().GetMethod(nameof(GetTexture), Instance)?.DeclaringType != typeof(AttackIntent);

    public override int GetTotalDamage(IEnumerable<Creature> targets, Creature owner) => _damage * Math.Max(1, Repeats);

    /// <summary>The game intent's own label (its format, its repeats), its damage the projected hit (LocString.Add sets a variable again).</summary>
    public override LocString GetIntentLabel(IEnumerable<Creature> targets, Creature owner)
    {
        var label = _intent.GetIntentLabel(targets, owner);
        label.Add("Damage", (decimal)_damage);
        return label;
    }

    /// <summary>AttackIntent's hover tip text (its title's description, multiplayer, its repeats), its damage the projected hit.</summary>
    protected override LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner)
    {
        var description = base.GetIntentDescription(targets, owner);
        description.Add("Damage", (decimal)_damage);
        return description;
    }

    public override Texture2D GetTexture(IEnumerable<Creature> targets, Creature owner) =>
        OwnLooks ? _intent.GetTexture(targets, owner) : base.GetTexture(targets, owner);

    public override string GetAnimation(IEnumerable<Creature> targets, Creature owner) =>
        OwnLooks ? _intent.GetAnimation(targets, owner) : base.GetAnimation(targets, owner);
}

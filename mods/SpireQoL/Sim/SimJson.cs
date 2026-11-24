using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SpireQoL.Sim;

/// <summary>
/// How the simulator's state is written as JSON (SimState.ToJson: reports' start states, replay cases) and what a hand card's
/// key is read from (SimHandCard.Key). Actions are written as what they are: each kind under its name in "$action", with every
/// member (an action written as its base type would be an empty object, and a replayed play would do nothing).
/// </summary>
public static class SimJson
{
    /// <summary>Every kind of action (each record deriving from SimAction), by name: found, never listed, so a new one is never left out.</summary>
    public static readonly IReadOnlyList<Type> ActionKinds = typeof(SimAction).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(SimAction).IsAssignableFrom(type))
        .OrderBy(type => type.Name, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Reports' start states and replay cases: every enum by its name (a script says "Exhausted", not 9, so it stays readable when an enum's numbers
    /// move); one written by number before reads the same.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ActionsByKind } },
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// What a hand card's key hashes (SimHandCard.Key): its enums as numbers, as the key always hashed them. A key is an identity, never read back,
    /// so the names change nothing but the hash, which orders branches (a hand card's key is in the branch key).
    /// </summary>
    public static readonly JsonSerializerOptions KeyOptions = new()
    {
        IncludeFields = true,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ActionsByKind } },
    };

    /// <summary>Every kind of condition (each record deriving from SimCondition), by name, found the same way.</summary>
    public static readonly IReadOnlyList<Type> ConditionKinds = typeof(SimCondition).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(SimCondition).IsAssignableFrom(type))
        .OrderBy(type => type.Name, StringComparer.Ordinal).ToList();

    private static void ActionsByKind(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(SimCondition))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "$condition",
                UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
            };
            foreach (var kind in ConditionKinds) typeInfo.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(kind, kind.Name));
            return;
        }
        if (typeInfo.Type != typeof(SimAction)) return;
        typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$action",
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var kind in ActionKinds) typeInfo.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(kind, kind.Name));
    }
}

/// <summary>
/// A creature id that may be no one (a play's target, who cast Hex): no one (Who.None) is written as null. Read, null is no one,
/// and so is -1, which meant no one in states written before Who.None (a target is never you, the other -1).
/// </summary>
public sealed class CreatureIdJson : JsonConverter<int>
{
    public override bool HandleNull => true;

    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? Who.None : reader.GetInt32() is var id && id == -1 ? Who.None : id;

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        if (value == Who.None) writer.WriteNullValue();
        else writer.WriteNumberValue(value);
    }
}

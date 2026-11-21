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

    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ActionsByKind } },
    };

    private static void ActionsByKind(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(SimAction)) return;
        typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$action",
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var kind in ActionKinds) typeInfo.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(kind, kind.Name));
    }
}

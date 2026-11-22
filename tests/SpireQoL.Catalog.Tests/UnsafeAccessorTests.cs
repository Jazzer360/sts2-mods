using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The mod reads a few of the game's private fields through the runtime's accessors (UnsafeAccessor: BoardProbe). A field a game update
/// renamed or retyped only fails when it's read, so every one is checked here against the installed game, on both branches' builds.
/// </summary>
public class UnsafeAccessorTests
{
    [Fact]
    public void EveryPrivateFieldTheModReadsExists()
    {
        var wrong = new List<string>();
        var checkedFields = 0;
        foreach (var type in typeof(SpireQoL.Combat.BoardProbe).Assembly.GetTypes())
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<UnsafeAccessorAttribute>() is not { Kind: UnsafeAccessorKind.Field } accessor) continue;
                checkedFields++;
                var owner = method.GetParameters()[0].ParameterType;
                var field = owner.GetField(accessor.Name!, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var wanted = method.ReturnType.GetElementType();
                if (field == null) wrong.Add($"{type.Name}.{method.Name}: {owner.Name}.{accessor.Name} is gone");
                else if (field.FieldType != wanted) wrong.Add($"{type.Name}.{method.Name}: {owner.Name}.{accessor.Name} is {field.FieldType.Name}, read as {wanted?.Name}");
            }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}

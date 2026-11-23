using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// An option that runs a delegate the event keeps (the Endless Conveyor's GrabSomethingOffTheBelt runs CurrentDish.action) is
/// read through the delegate the event holds: Fried Eel adds a card (CardPileCmd.Add to the deck).
/// </summary>
public class HeldDelegateTests
{
    private const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void TheConveyorsCurrentDishIsRead()
    {
        var conveyorType = typeof(EventModel).Assembly.GetTypes().Single(type => type.Name == "EndlessConveyor");
        var conveyor = RuntimeHelpers.GetUninitializedObject(conveyorType);
        var dishField = conveyorType.GetField("_currentDish", F)!;
        object dish = RuntimeHelpers.GetUninitializedObject(dishField.FieldType);
        var friedEel = (Func<Task>)Delegate.CreateDelegate(typeof(Func<Task>), conveyor, conveyorType.GetMethod("FriedEel", F)!);
        dishField.FieldType.GetField("action", F)!.SetValue(dish, friedEel);
        dishField.SetValue(conveyor, dish);

        var grab = (Func<Task>)Delegate.CreateDelegate(typeof(Func<Task>), conveyor, conveyorType.GetMethod("GrabSomethingOffTheBelt", F)!);
        var option = (EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption));
        var onChosen = typeof(EventOption).GetField("<OnChosen>k__BackingField", F);
        Assert.NotNull(onChosen);
        onChosen!.SetValue(option, grab);

        var effects = RunReminders.EffectsOf(option);
        Assert.True(effects.HasFlag(ChoiceEffect.CardToDeck), $"read {effects}");
    }
}

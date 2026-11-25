using System.Reflection;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using SpireQoL.Data;
using SpireQoL.Diagnostics;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The cards on offer outside fights are searched only when what could change them does (RunReminders.OfferedCards, decision #177), and
/// dev mode's check of that compares what's kept with a fresh search. What the game does to the cards on screen with no press and no change
/// of screen, read from its code: hovering a card brings its holder to the front of its row, and a grid of cards shows and hides its
/// recycled holders as it scrolls.
/// </summary>
public class OfferedCardsTests
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static bool Calls(Type type, string method, string called) =>
        GameCode.Instructions(type.GetMethod(method, Declared)!).Any(instruction => instruction.operand is MethodInfo { Name: var name } && name == called);

    [Fact]
    public void TheSameCardsInAnotherOrderAreTheSameOffer()
    {
        // A dev review: "the cards on offer changed with no change of screen and no press", on a card reward the player was hovering.
        Assert.True(Calls(typeof(NGridCardHolder), "OnFocus", "MoveToFrontSafely"));
        object strike = new(), defend = new(), bash = new();
        var searched = new List<(object Node, ReminderPlace Place)> { (strike, ReminderPlace.Reward), (bash, ReminderPlace.Reward), (defend, ReminderPlace.Reward) };
        var kept = new List<(object Node, ReminderPlace Place)> { (strike, ReminderPlace.Reward), (defend, ReminderPlace.Reward), (bash, ReminderPlace.Reward) };
        Assert.True(RunReminders.SameOffer(searched, kept));

        // A card more, a card fewer, or one somewhere else is a change.
        Assert.False(RunReminders.SameOffer(searched, kept.Take(2).ToList()));
        Assert.False(RunReminders.SameOffer(searched.Take(2).ToList(), kept));
        kept[2] = (bash, ReminderPlace.Shop);
        Assert.False(RunReminders.SameOffer(searched, kept));
    }

    [Fact]
    public void AGridOfCardsShowsItsHoldersAgainAsItScrolls()
    {
        // NCardGrid.AssignCardsToRow, as the grid glides on after a scroll: the holders past its last card hidden, the others shown again with
        // the card now on them (the same NCard, its model changed: ReassignToCard). The kept cards are every card found, shown or not.
        Assert.True(Calls(typeof(NCardGrid), "AssignCardsToRow", "set_Visible"));
        Assert.True(Calls(typeof(NCardGrid), "AssignCardsToRow", nameof(NCardHolder.ReassignToCard)));
        Assert.True(Calls(typeof(NCardHolder), nameof(NCardHolder.ReassignToCard), "set_Model"));
    }
}

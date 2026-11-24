using Godot;
using SpireQoL.Cards;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A tiny card's fit (decision #177): walking its nodes makes garbage, so it runs only until the drawn area stops moving, then not again
/// until the card's size changes.
/// </summary>
public class TinyCardFitTests
{
    [Fact]
    public void TheFitSettlesOnceTheDrawnAreaStaysTheSame()
    {
        Rect2? last = null;
        var sameFits = 0;
        var box = new Rect2(x: 1, y: 2, width: 30, height: 40);
        for (var i = 1; i < TinyCardIcon.FitsToSettle; i++) Assert.False(TinyCardIcon.Settles(ref last, ref sameFits, box));
        Assert.True(TinyCardIcon.Settles(ref last, ref sameFits, box));
    }

    [Fact]
    public void AnAreaThatMovesStartsTheCountAgain()
    {
        Rect2? last = null;
        var sameFits = 0;
        var box = new Rect2(x: 1, y: 2, width: 30, height: 40);
        for (var i = 1; i < TinyCardIcon.FitsToSettle; i++) TinyCardIcon.Settles(ref last, ref sameFits, box);
        // The art came in a frame late: the area moved.
        Assert.False(TinyCardIcon.Settles(ref last, ref sameFits, new Rect2(x: 0, y: 0, width: 32, height: 44)));
        Assert.Equal(1, sameFits);
    }
}

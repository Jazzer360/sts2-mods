using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Cards;
using SpireQoL.Procs;
using Sts2Mods.Core.Game;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #173: what runs every frame on a board where nothing changed makes no garbage (each frame's few kilobytes, at the game's
/// frame rate, set off a collection every second or so). Measured on the game's own objects, after a first call warms up.
/// </summary>
[Collection(GameFightCollection.Name)]
public class PerFrameMemoryTests
{
    /// <summary>
    /// The fewest bytes a call over three rounds: garbage the call makes lands in every round, a one-off of the runtime's in one (a
    /// method compiled again at a higher tier while Harmony's patching hook is in the process: another test patches a game method).
    /// </summary>
    internal static long BytesPerCall(Action call, int calls = 200)
    {
        call();
        var fewest = long.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < calls; i++) call();
            fewest = Math.Min(fewest, (GC.GetAllocatedBytesForCurrentThread() - start) / calls);
        }
        return fewest;
    }

    [Fact]
    public void FreeCardsWatchingYourHandMakesNothing()
    {
        var gameFight = new GameFight();
        for (var i = 0; i < 8; i++) gameFight.Add<StrikeIronclad>();
        var hand = Sts2State.Hand(gameFight.Me);
        var perCall = BytesPerCall(() => FreeCards.Observe(gameFight.Me, hand));
        Assert.True(perCall == 0, $"{perCall} bytes a frame");
    }

    [Fact]
    public void TheRestSitesBossCheckAsksWhatChangedWithoutGarbage()
    {
        // A dev review: at the fire, the boss start's prediction (Pantograph, Bath Robes) was worked out again every frame, 1.7 KB each; it's
        // worked out again only when its key (your HP and relics) changes, and reading the key makes nothing.
        var gameFight = new GameFight();
        var perCall = BytesPerCall(() => SpireQoL.Features.RestPreviewFeature.PlayerKey(gameFight.Me));
        Assert.True(perCall == 0, $"{perCall} bytes a frame");
    }

    [Fact]
    public void ReadingAPrivateMemberMakesNothing()
    {
        // Sts2State.Private: the member is found once per type and name (it made three Traverse objects and a name every call).
        var gameFight = new GameFight();
        var card = gameFight.Add<StrikeIronclad>();
        var perCall = BytesPerCall(() => Sts2State.Private<object?>(card.EnergyCost, "_localModifiers", fallback: null));
        Assert.True(perCall == 0, $"{perCall} bytes a read");
    }

    [Fact]
    public void CardDecorationsAreKeptBetweenFrames()
    {
        // CardDecorFrame: a card's decor comes back from the frame before, and last frame's tabs keep their list while they're the
        // same (one made only when they change, never changed in place: the audits keep what they read).
        var gameFight = new GameFight();
        var card = gameFight.Add<StrikeIronclad>();
        var frame = new CardDecorFrame();
        void Frame(string key)
        {
            frame.SeenInHand(card);
            frame.For(card).AddTab(new TabSpec(key, Icon: null));
            frame.Clear();
        }
        Frame("proc:A");
        var before = frame.Previous[card];
        Frame("proc:A");
        Assert.Same(before, frame.Previous[card]);
        Frame("proc:B");
        Assert.NotSame(before, frame.Previous[card]);
        Assert.Equal("proc:A", before[0].Key); // the list read before is as it was
        var spec = new TabSpec("proc:B", Icon: null);
        var perFrame = BytesPerCall(() =>
        {
            frame.SeenInHand(card);
            frame.For(card).AddTab(spec);
            frame.Clear();
        });
        Assert.True(perFrame == 0, $"{perFrame} bytes a frame");
    }

    [Fact]
    public void AskingWhetherATabIsOnMakesNothing()
    {
        // Every tab shown asks every frame (CardDecor's filter): its group's key ("proc:*") was built as two strings each time.
        var settings = new QolSettings();
        var perCall = BytesPerCall(() => settings.TabEnabled("proc:ThornsPower"));
        Assert.True(perCall == 0, $"{perCall} bytes a tab");
    }
}

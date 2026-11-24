using MegaCrit.Sts2.Core.Models;
using SpireQoL.Diagnostics;
using SpireQoL.Procs.Programs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Every card's program is read once a launch, on a worker as the mod loads (ModEntry.StartReadingCode), so a fight never reads one on the
/// game's thread: reading them all fills the cache a fight asks.
/// </summary>
public class CardProgramLoadTests
{
    [Fact]
    public void ReadingEveryCardAtLoadFillsTheCacheAFightAsks()
    {
        var cards = CoverageLedger.ModelTypes().Where(type => typeof(CardModel).IsAssignableFrom(type)).ToList();
        Assert.True(cards.Count > 500, $"only {cards.Count} card classes found");
        var before = cards.Select(CardPrograms.Of).ToList();

        Assert.Equal(cards.Count, CardPrograms.ReadAll(cards));

        // The same program objects: nothing is read again once it's in the cache.
        Assert.All(cards.Zip(before), pair => Assert.Same(pair.Second, CardPrograms.Of(pair.First)));
    }
}

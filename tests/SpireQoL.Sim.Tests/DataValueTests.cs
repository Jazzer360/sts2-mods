using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power's or relic's Data holds what its model needs beside its amount: a flag, a counter, a creature. The adapter writes it and
/// the model reads it, so a value with a meaning has one name both use (decision #143): a shared one (RelicUse, PowerUse,
/// DataCreature) or a constant in the model. A bare number in either place fails here; 0 (nothing) is fine.
/// </summary>
public class DataValueTests
{
    private static readonly Regex[] BareValues =
    {
        new(@"\bdata: [1-9]"),                                                                     // written: data: 1
        new(@"(?:\bData\([^()]*\)|\bDataOf\([^()]*\)|\bdata\b)\s*(?:[&|^]|[=!<>]=?)\s*~?[1-9]"),  // tested or combined: Data(state, owner) & 2
        new(@"[+-] 100\b"),                                                                        // a creature by hand: id + 100
        new(@"\bdata\b.*\?\s*[1-9]\s*:\s*0"),                                                     // the adapter's: data = flag ? 1 : 0
    };

    [Fact]
    public void DataValuesHaveNames()
    {
        var found = RepoFiles.Sources("mods/SpireQoL/Sim/Models").Concat(RepoFiles.Sources("mods/SpireQoL/Sim/Adapter"))
            .SelectMany(source => source.Lines.Select((line, index) => (source.Path, Line: index + 1, Text: line.Trim())))
            .Where(line => !line.Text.StartsWith("//", StringComparison.Ordinal))
            .Where(line => BareValues.Any(pattern => pattern.IsMatch(line.Text)))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text}").ToList();
        Assert.True(found.Count == 0, "Bare numbers in a power's or relic's Data (name each: RelicUse, PowerUse, DataCreature, or a constant in the model):\n" + string.Join("\n", found));
    }

    [Fact]
    public void ACreatureInDataIsItsIdOrNoOne()
    {
        foreach (var id in new[] { 0, 3, Who.You, Who.Teammate(teammateIndex: 0), Who.Teammate(teammateIndex: 2) })
            Assert.Equal(id, DataCreature.In(DataCreature.Of(id)));
        Assert.Equal(0, DataCreature.Of(id: null));
        Assert.Equal(0, DataCreature.Of(Who.None));
        Assert.Equal(Who.None, DataCreature.In(data: 0));
    }
}

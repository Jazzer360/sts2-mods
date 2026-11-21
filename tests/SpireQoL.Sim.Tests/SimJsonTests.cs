using System.Collections;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What the state's JSON holds (SimJson): every action as what it is, with every member, so a report's start state replays as it
/// ran, and a hand card's key (SimHandCard.Key, decision #140) reads everything the simulator does with the card. Built by
/// reflection over every action kind and every field of a play, with a value that isn't the default, so a new action or field is
/// covered without listing it; a member of a type the sampler doesn't know fails here, to be taught.
/// </summary>
public class SimJsonTests
{
    [Fact]
    public void EveryActionRoundTripsWithEveryMember()
    {
        Assert.NotEmpty(SimJson.ActionKinds);
        Assert.Equal(SimJson.ActionKinds.Count, SimJson.ActionKinds.Select(kind => kind.Name).Distinct().Count()); // "$action" names are unique
        foreach (var kind in SimJson.ActionKinds)
        {
            var action = (SimAction)Sample(kind);
            var json = JsonSerializer.Serialize(action, SimJson.Options);
            var read = JsonSerializer.Deserialize<SimAction>(json, SimJson.Options)!;
            Assert.Equal(kind, read.GetType());
            Assert.Equal(json, JsonSerializer.Serialize(read, SimJson.Options)); // every value came back
            using var document = JsonDocument.Parse(json);
            foreach (var member in Members(kind))
                Assert.True(document.RootElement.TryGetProperty(member.Name, out _), $"{kind.Name}.{member.Name} isn't written");
        }
    }

    [Fact]
    public void AStateWithAnAutoHandReplaysItsActions()
    {
        var state = new SimState();
        state.You.AutoHand = new() { new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { (SimPlay)Sample(typeof(SimPlay)) } } };
        var json = state.ToJson();
        Assert.Equal(json, SimState.FromJson(json).ToJson());
        Assert.Contains("\"$action\"", json);
    }

    [Fact]
    public void AHandCardsKeyReadsEveryFieldOfItsPlaysButNotWhichCardItIs()
    {
        SimHandCard Card(int id, Action<SimPlay>? change = null)
        {
            var play = new SimPlay { HandCard = id };
            change?.Invoke(play);
            return new SimHandCard { Id = id, Name = "Strike", IsAttack = true, Cost = 1, Plays = { play } };
        }
        var key = Card(id: 11).Key;
        Assert.Equal(key, Card(id: 12).Key); // another copy: its id and its plays' HandCard aren't what it is
        Assert.NotEqual(key, new SimHandCard { Id = 11, Name = "Bash", IsAttack = true, Cost = 1, Plays = { new SimPlay { HandCard = 11 } } }.Key);
        Assert.NotEqual(key, new SimHandCard { Id = 11, Name = "Strike", IsAttack = false, Cost = 1, Plays = { new SimPlay { HandCard = 11 } } }.Key);
        Assert.NotEqual(key, new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 0, Plays = { new SimPlay { HandCard = 11 } } }.Key);
        Assert.NotEqual(key, new SimHandCard { Id = 11, Name = "Strike", IsAttack = true, Cost = 1, Plays = { new SimPlay { HandCard = 11 }, new SimPlay { HandCard = 11 } } }.Key);
        Assert.NotEqual(key, Card(id: 11, play => play.HandCard = 0).Key); // a play that isn't this card's own is something else
        foreach (var field in typeof(SimPlay).GetFields(BindingFlags.Instance | BindingFlags.Public).Where(field => field.Name != nameof(SimPlay.HandCard)))
            Assert.True(key != Card(id: 11, play => field.SetValue(play, Sample(field.FieldType))).Key, $"SimPlay.{field.Name} isn't in a hand card's key");
    }

    [Fact]
    public void ADrawnCopyHasTheKeyOfTheCardItCopies()
    {
        var card = new SimHandCard { Name = "Strike", IsAttack = true, Cost = 1, Plays = { new SimPlay { Hits = 1 } } };
        var drawn = card.CopyWithId(-1001);
        Assert.Equal(-1001, drawn.Id);
        Assert.Equal(card.Key, drawn.Key);
        Assert.Equal(card.Key, new SimHandCard { Id = -1001, Name = "Strike", IsAttack = true, Cost = 1, Plays = card.Plays }.Key); // as if read from scratch
    }

    private static IEnumerable<MemberInfo> Members(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.GetIndexParameters().Length == 0 && property.Name != "EqualityContract")
            .Cast<MemberInfo>().Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public));

    /// <summary>A value of a type that isn't its default: collections hold one sample, objects have every member set.</summary>
    private static object Sample(Type type, int depth = 0)
    {
        if (depth > 6) throw new InvalidOperationException($"{type.Name} nests too deep to sample");
        if (Nullable.GetUnderlyingType(type) is { } inner) return Sample(inner, depth);
        if (type == typeof(int)) return 3;
        if (type == typeof(long)) return 4L;
        if (type == typeof(decimal)) return 2.5m;
        if (type == typeof(double)) return 1.5;
        if (type == typeof(bool)) return true;
        if (type == typeof(string)) return "Probe";
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(values.Length > 1 ? 1 : 0)!;
        }
        if (type.GetMethod("Of", BindingFlags.Static | BindingFlags.Public, [typeof(string)]) is { } named && named.ReturnType == type) return named.Invoke(null, ["Probe"])!;
        if (type == typeof(SimAction)) return new HitYouAction(Name: "Probe", Amount: 3);
        if (type.IsGenericType && type.Name.StartsWith("ValueTuple", StringComparison.Ordinal))
            return Activator.CreateInstance(type, type.GetGenericArguments().Select(element => Sample(element, depth + 1)).ToArray())!;
        if (type.IsGenericType && type.GetGenericArguments() is [var keyType, var valueType] && typeof(IEnumerable).IsAssignableFrom(type) && type.Name.Contains("Dictionary"))
        {
            var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType))!;
            dictionary.Add(Sample(keyType, depth + 1), Sample(valueType, depth + 1));
            return dictionary;
        }
        if (type.IsGenericType && type.GetGenericArguments() is [var elementType] && typeof(IEnumerable).IsAssignableFrom(type))
        {
            // The collection itself when it can be made (a HashSet), else a list (an IReadOnlyList).
            var collection = Activator.CreateInstance(type.IsInterface || type.IsAbstract ? typeof(List<>).MakeGenericType(elementType) : type)!;
            collection.GetType().GetMethod("Add", [elementType])!.Invoke(collection, [Sample(elementType, depth + 1)]);
            return collection;
        }
        if (type.IsAbstract || type.IsInterface || type.IsPrimitive) throw new InvalidOperationException($"teach Sample to make a {type.Name}");
        var constructor = type.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).First();
        var sample = constructor.Invoke(constructor.GetParameters().Select(parameter => Sample(parameter.ParameterType, depth + 1)).ToArray());
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(field => !field.IsInitOnly)) field.SetValue(sample, Sample(field.FieldType, depth + 1));
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.CanWrite && property.GetIndexParameters().Length == 0))
            property.SetValue(sample, Sample(property.PropertyType, depth + 1));
        return sample;
    }
}

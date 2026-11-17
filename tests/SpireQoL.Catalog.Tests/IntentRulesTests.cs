using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Intent Forecast (decision #72) reads every enemy's move rules (IntentReader): each enemy in the installed game builds its
/// move state machine (as a fight copy does) and the reader turns it into the forecast model. A new enemy, or a new kind of
/// move state the forecast doesn't know, fails here before anyone fights it.
/// </summary>
public class IntentRulesTests
{
    private static readonly MethodInfo Generate =
        typeof(MonsterModel).GetMethod("GenerateMoveStateMachine", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void EveryEnemysMoveRulesAreRead()
    {
        GameModels.EnsureLoaded();
        var monsters = ModelDb.All.OfType<MonsterModel>().ToList();
        Assert.True(monsters.Count > 100, "the game's enemies didn't load");
        var unread = new List<string>();
        foreach (var m in monsters)
        {
            MonsterMoveStateMachine machine;
            try { machine = (MonsterMoveStateMachine)Generate.Invoke(m.ToMutable(), null)!; }
            catch (Exception e) { unread.Add($"{m.GetType().Name}: rules not built ({(e.InnerException ?? e).Message})"); continue; }
            if (IntentReader.Graph(machine) == null) unread.Add($"{m.GetType().Name}: rules not read");
        }
        Assert.True(unread.Count == 0, "Enemies whose move rules the forecast can't read:\n" + string.Join("\n", unread));
    }
}

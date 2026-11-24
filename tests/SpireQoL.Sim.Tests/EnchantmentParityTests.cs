using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The enchantments read from their classes' code (decision #217: SimCard.Enchantment, run by the engine after each play's own effects) against the
/// adapter's per-name rules they replaced, frozen here as the actions those rules added to the play (SimAdapter.Play at a0666b2): Adroit's block,
/// Corrupted's 2 HP, Inky's Weak on the target or every enemy, Swift's draw on the first play only, Sown's energy before the play. 300 generated
/// fights each: one to three enemies, your block, Juggernaut, Plating and Dexterity, a draw pile, a card that attacks or blocks, played once or
/// twice, the enchantment Normal or Disabled; every branch's outcome (HP taken, block, energy, hand, draws, each enemy's HP, block and Weak, its
/// weight) must be the same. Sown's energy now comes after the card's own effects, as the game gives it (CardModel.OnPlayWrapper: OnPlay, then
/// Enchantment.OnPlay); nothing on these boards reads energy in between.
/// </summary>
public class EnchantmentParityTests
{
    private const int Scenarios = 300;

    private static readonly SimDrawCard Plain = new("plain");

    /// <summary>The frozen rule's actions for one enchantment (Before: Sown's, ahead of the card play; Body: after the card's own).</summary>
    private static (List<SimAction> Before, List<SimAction> Body) Frozen(string enchantment, int amount, bool normal, SimPlay.Target targets) => enchantment switch
    {
        "Adroit" => (new(), new() { new GainBlockAction(Times: 1, amount, SimProps.Move) }),
        "Corrupted" => (new(), new() { new LoseHpAction(2) }),
        "Inky" => (new(), new() { new ApplyPowerAction(PowerId.Weak, 1) { To = targets == SimPlay.Target.All ? ApplyPowerAction.Where.AllEnemies : ApplyPowerAction.Where.Target, Debuff = true } }),
        "Swift" => (new(), normal ? new() { new FirstPlayOnly(new SimAction[] { new CardEventAction(CardEvent.Drawn, amount) { Indirect = true, JoinsHand = true } }) } : new()),
        "Sown" => (normal ? new() { new CardEventAction(CardEvent.EnergyGained, amount) } : new(), new()),
        _ => throw new ArgumentException(enchantment),
    };

    [Theory]
    [InlineData("Adroit")]
    [InlineData("Corrupted")]
    [InlineData("Inky")]
    [InlineData("Swift")]
    [InlineData("Sown")]
    public void TheReadEnchantmentDoesWhatTheRuleDid(string enchantment)
    {
        Assert.NotNull(SimModels.Enchantment(enchantment));
        var differ = Enumerable.Range(0, Scenarios).Where(scenario => Outcome(enchantment, scenario, read: true) != Outcome(enchantment, scenario, read: false)).ToList();
        Assert.True(differ.Count == 0, $"{enchantment}: {differ.Count} of {Scenarios} outcomes differ (first: {string.Join(", ", differ.Take(10))})");
        // And the boards tell it apart from no enchantment at all (the comparison isn't empty).
        Assert.True(Enumerable.Range(0, Scenarios).Count(scenario => Outcome(enchantment, scenario, read: true) != Outcome(enchantment, scenario, read: true, plain: true)) > Scenarios / 4);
    }

    /// <summary>
    /// Corrupted.OnPlay damages its card's owner (CreatureCmd.Damage(base.Card.Owner.Creature, 2, Unblockable | Unpowered | Move)): a teammate's
    /// Corrupted card hurts that teammate, not you (the per-name rule left a teammate's out, as not modelled).
    /// </summary>
    [Fact]
    public void ATeammatesCorruptedCardHurtsThem()
    {
        var state = new SimState { Enemies = { ScriptBuilder.Enemy(id: 0, hp: 40) } };
        state.Teammates.Add(new SimPlayer { Hp = 30, MaxHp = 30, HandCount = 3 });
        var card = ScriptBuilder.Card("Enchanted") with { Targets = SimPlay.Target.Single, HandId = 1, Enchantment = new SimEnchantment("Corrupted", Amount: 1) };
        var script = new SimScript { Name = card.Name, Actions = { new CardPlayAction(new SimCardPlay(card, Who.Teammate(0), Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false), new SimAction[] { ScriptBuilder.Attack(baseDamage: 6) }) } };
        var outcome = SimEngine.Run(state, script);
        Assert.True(outcome.InEvery(branch => branch.Teammates[0].Taken == 2 && branch.You.Taken == 0 && branch.Unmodelled == null));
    }

    private static string Outcome(string enchantment, int index, bool read, bool plain = false)
    {
        var random = new Random(9100 + index);
        var state = new SimState();
        var enemies = random.Next(1, 4);
        for (var i = 0; i < enemies; i++)
            state.Enemies.Add(new SimEnemy { Id = i, Name = $"E{i}", Hp = random.Next(1, 40), Block = random.Next(0, 3) == 0 ? random.Next(1, 10) : 0, Hits = Enumerable.Range(0, random.Next(0, 3)).Select(_ => random.Next(1, 12)).ToList() });
        var you = state.You;
        you.Hp = you.MaxHp = random.Next(5, 60);
        you.Block = random.Next(0, 3) == 0 ? random.Next(1, 8) : 0;
        you.Energy = random.Next(0, 4);
        you.HandCount = random.Next(1, 9);
        if (random.Next(3) == 0) you.Juggernaut = random.Next(1, 5);
        if (random.Next(3) == 0) you.Plating = random.Next(1, 5);
        if (random.Next(3) == 0) you.Powers = you.Powers.With(PowerId.Dexterity, random.Next(-2, 3) is var dexterity and not 0 ? dexterity : 1);
        if (random.Next(2) == 0) you.Draw = new SimDrawPile { Rest = { new SimDrawGroup(Plain, Count: random.Next(0, 6)) }, Discard = { new SimDrawGroup(Plain, Count: random.Next(0, 4)) } };
        var targets = random.Next(2) == 0 ? SimPlay.Target.Single : SimPlay.Target.All;
        var amount = random.Next(1, 6);
        var normal = random.Next(4) != 0;
        var plays = random.Next(1, 3);
        var target = targets == SimPlay.Target.Single ? random.Next(enemies) : Who.None;
        SimAction own = random.Next(2) == 0 ? ScriptBuilder.Attack(random.Next(1, 15), mode: targets) : ScriptBuilder.Block(random.Next(1, 10));
        var card = ScriptBuilder.Card("Enchanted", own is AttackAction ? SimCardType.Attack : SimCardType.Skill) with { Targets = targets, HandId = 1 };
        var script = new SimScript { Name = card.Name };
        if (read)
        {
            if (!plain) card = card with { Enchantment = new SimEnchantment(enchantment, amount) { Fields = NamedCounts.Empty.With("Status", normal ? 0 : 1) } };
            script.Actions.Add(new CardPlayAction(new SimCardPlay(card, Who.You, target, PlayIndex: 0, plays, AutoPlay: false), new[] { own }));
        }
        else
        {
            var (before, body) = Frozen(enchantment, amount, normal, targets);
            script.Actions.AddRange(before);
            script.Actions.Add(new CardPlayAction(new SimCardPlay(card, Who.You, target, PlayIndex: 0, plays, AutoPlay: false), new[] { own }.Concat(body).ToArray()));
        }
        var outcome = SimEngine.Run(state, script);
        var branches = outcome.Branches.Select(branch =>
            $"{branch.Taken},{branch.You.Block},{branch.BlockAtEnemyTurn},{branch.You.Energy},{branch.You.HandCount},{branch.You.Drawn},{branch.Unmodelled != null},{branch.Weight.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}|"
            + string.Join(";", branch.Enemies.OrderBy(enemy => enemy.Id).Select(enemy => $"{enemy.Id}:{enemy.Hp},{enemy.Block},{enemy.Powers[PowerId.Weak]}")))
            .Order(StringComparer.Ordinal);
        return new StringBuilder($"T{outcome.Truncated} ").AppendJoin("#", branches).ToString();
    }
}

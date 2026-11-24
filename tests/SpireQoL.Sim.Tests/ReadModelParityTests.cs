using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Each model read from the game's code (decision #195) against the hand-written model it replaced (HandWrittenModels.cs, kept as it was): every
/// hook either answers, called the same way on the same boards (the owner you, a teammate or an enemy; plays of every type, cost and owner;
/// every card event for you and for another; powers landing of each kind from each applier; damage and block of every dealer, target, kind and
/// source), must leave the same branches (each one's state key, weight and play log) or return the same number. A difference the read model
/// makes on purpose (it follows a line of the game's code the hand-written one left out) is reviewed below with that line, and nothing else is.
/// </summary>
public class ReadModelParityTests
{
    private static readonly Dictionary<string, SimModel> HandWritten = typeof(ReadModelParityTests).Assembly.GetTypes()
        .Where(type => type.Namespace == typeof(HandWritten.StrengthPower).Namespace && !type.IsAbstract && typeof(SimModel).IsAssignableFrom(type))
        .Select(type => (SimModel)Activator.CreateInstance(type)!).ToDictionary(model => model.Name, StringComparer.Ordinal);

    /// <summary>
    /// Differences reviewed: "Model.Hook" → the game's line the read model follows and the hand-written one didn't, and why it changes no
    /// prediction (or which, a fix).
    /// </summary>
    private static readonly Dictionary<string, string> Reviewed = new(StringComparer.Ordinal)
    {
        ["DoubleDamagePower.AfterSideTurnEnd"] = "PowerCmd.Decrement(this) when its owner takes part: it goes down as your turn ends. After it no card of yours plays this turn (the auto-plays come before), so no damage it doubles is left: no prediction changes",
        ["RagePower.AfterSideTurnEnd"] = "PowerCmd.Remove(this) when its owner takes part: it goes as your turn ends. No card of yours plays after it (the auto-plays come before): no prediction changes",
        ["CorrosiveWavePower.AfterSideTurnEnd"] = "PowerCmd.Remove(this) when its owner takes part: it goes as your turn ends. You draw nothing after it this turn: no prediction changes",
        ["SleightOfFleshPower.AfterPowerApplied"] = "!(power is ITemporaryPower): a temporary power landing (Dark Shackles) doesn't set it off, the Strength its BeforeApplied applies does. The simulator lands a temporary Strength power as that Strength (SimAdapter.PowersFrom), which both count: no prediction changes",
        ["ShroudPower.AfterPowerApplied"] = "applier == base.Owner && power is DoomPower, with no test of the target: Doom you put on a player sets it off too (the hand-written model wanted an enemy). Nothing of yours puts Doom on a player today",
        ["ViciousPower.AfterPowerApplied"] = "amount > 0 && applier == base.Owner && power is VulnerablePower, with no test of the target: Vulnerable you put on a player sets it off too (the hand-written model wanted an enemy). Nothing of yours puts Vulnerable on a player today",
    };

    /// <summary>
    /// The relics whose hand-written model's amount the adapter read from one of their values (SimReaders.RelicAmountVars), by that value's key:
    /// the board gives the amount as the adapter did, so the two read the same number.
    /// </summary>
    private static readonly Dictionary<string, string> RelicAmountVars = new(StringComparer.Ordinal) { [RelicId.CharonsAshes.Name] = "Damage", [RelicId.ForgottenSoul.Name] = "Damage" };

    public static TheoryData<string> Models() => new(HandWritten.Keys.OrderBy(name => name, StringComparer.Ordinal));

    [Fact]
    public void EveryReadModelReplacedAHandWrittenOne()
    {
        var read = ReadModelsFile.Specs().Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(HandWritten.Keys.OrderBy(name => name, StringComparer.Ordinal), read.Intersect(HandWritten.Keys).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(HandWritten.Keys, name => Assert.IsType<ReadModel>(SimModels.Get(name)));
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void TheReadModelDoesWhatTheHandWrittenOneDid(string name)
    {
        var hand = HandWritten[name];
        var read = Assert.IsType<ReadModel>(SimModels.Get(name));
        Assert.Equal(hand.Tab, read.Tab);
        Assert.Equal(hand.Harmful, read.Harmful);
        Assert.Equal(hand.Seen, read.Seen);
        Assert.Equal(hand.Instanced, read.Instanced);
        Assert.Equal(hand.JudgedOnOutcome, read.JudgedOnOutcome);
        var differences = new List<string>();
        var compared = 0;
        foreach (var hook in Hooks.Where(hook => hand.Overrides(hook) || read.Overrides(hook)))
        {
            var difference = new List<string>();
            foreach (var owner in OwnersOf(read))
                foreach (var (scenario, call) in Calls(hook, owner))
                {
                    compared++;
                    var handResult = call(hand, Board(read, owner));
                    var readResult = call(read, Board(read, owner));
                    if (handResult != readResult) difference.Add($"{name}.{hook} owner {owner}, {scenario}:\n  hand: {handResult}\n  read: {readResult}");
                }
            if (difference.Count == 0) continue;
            if (Reviewed.ContainsKey($"{name}.{hook}")) continue;
            differences.AddRange(difference.Take(4));
        }
        Assert.True(compared > 0, $"{name}: no scenario compared");
        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    [Fact]
    public void EveryReviewedDifferenceIsOne()
    {
        foreach (var key in Reviewed.Keys)
        {
            var (name, hook) = (key[..key.IndexOf('.', StringComparison.Ordinal)], key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            Assert.True(HandWritten.ContainsKey(name), $"{key}: no hand-written {name}");
            var read = Assert.IsType<ReadModel>(SimModels.Get(name));
            var differs = OwnersOf(read).SelectMany(owner => Calls(hook, owner).Select(entry => (owner, entry.Call)))
                .Any(entry => entry.Call(HandWritten[name], Board(read, entry.owner)) != entry.Call(read, Board(read, entry.owner)));
            Assert.True(differs, $"{key} is reviewed, but the read model does what the hand-written one did there");
        }
    }

    // ---- the boards

    /// <summary>The hooks a model can answer (SimModel's), the ones with scenarios here.</summary>
    private static readonly string[] Hooks =
    {
        nameof(SimModel.ModifyDamageAdditive), nameof(SimModel.ModifyDamageMultiplicative), nameof(SimModel.ModifyDamageMultiplicativeLate),
        nameof(SimModel.ModifyBlockAdditive), nameof(SimModel.ModifyBlockMultiplicative),
        nameof(SimModel.BeforeCardPlayed), nameof(SimModel.AfterCardPlayed), nameof(SimModel.AfterCardEvent), nameof(SimModel.AfterPowerApplied),
        nameof(SimModel.AfterSideTurnEnd),
    };

    [Fact]
    public void EveryHookAReadModelAnswersHasScenarios()
    {
        var answered = ReadModelsFile.Specs().SelectMany(spec => spec.Hooks).Select(program => ReadModel.SimHookOf(program.Hook)).Distinct();
        Assert.All(answered, hook => Assert.Contains(hook, Hooks));
    }

    private static IEnumerable<int> OwnersOf(ReadModel model) => model.Spec.Kind == ModelKind.Relic ? new[] { Who.You, Who.Teammate(0) } : new[] { Who.You, Who.Teammate(0), 0 };

    /// <summary>
    /// A board with the model on <paramref name="owner"/> (a power at 3, a relic armed), its values as the adapter reads them (each one its
    /// code reads, one more than its canonical value, so the state's is what's read), you and a teammate with block and cards, two enemies.
    /// </summary>
    private static SimState Board(ReadModel model, int owner)
    {
        var state = new SimState();
        state.You = new SimPlayer { Hp = 50, MaxHp = 70, Block = 4, HandCount = 3, Energy = 3 };
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, Block = 0, HandCount = 2, Energy = 2 });
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 30, Hits = new List<int> { 6 } });
        state.Enemies.Add(new SimEnemy { Id = 1, Name = "E1", Hp = 25, Block = 5, Hits = new List<int> { 3, 3 } });
        if (model.Spec.Kind == ModelKind.Power) SimEngine.SetPowers(state, owner, SimEngine.PowersOf(state, owner).With(new PowerId(model.Name), amount: 3));
        foreach (var variable in VarsOf(model.Spec))
            state.Vars[$"{owner}:{model.Name}:{variable.Key}"] = variable.Key.EndsWith(".Props", StringComparison.Ordinal) ? (int)variable.Canonical : (int)variable.Canonical + 1;
        var amount = RelicAmountVars.TryGetValue(model.Name, out var amountVar) ? state.Vars[$"{owner}:{model.Name}:{amountVar}"] : 0;
        if (model.Spec.Kind == ModelKind.Relic) SimEngine.SetRelics(state, owner, SimEngine.RelicsOf(state, owner).With(new RelicId(model.Name), amount, data: (int)RelicUse.Armed));
        return state;
    }

    private static IEnumerable<HookVar> VarsOf(ModelSpec spec) => spec.Values().OfType<HookVar>().DistinctBy(variable => variable.Key);

    // ---- the calls, each a hook on a board, its result as text

    private static readonly SimCardType[] Types = { SimCardType.Attack, SimCardType.Skill, SimCardType.Power, SimCardType.Status, SimCardType.Curse };

    private static IEnumerable<(string Scenario, Func<SimModel, SimState, string> Call)> Calls(string hook, int owner)
    {
        var others = new[] { owner, owner == Who.You ? Who.Teammate(0) : Who.You, 0 }.Distinct().ToArray();
        switch (hook)
        {
            case nameof(SimModel.ModifyDamageAdditive) or nameof(SimModel.ModifyDamageMultiplicative) or nameof(SimModel.ModifyDamageMultiplicativeLate):
                foreach (var damage in Damages(owner))
                    yield return ($"{damage}", (model, state) => Number(hook switch
                    {
                        nameof(SimModel.ModifyDamageAdditive) => model.ModifyDamageAdditive(state, owner, damage),
                        nameof(SimModel.ModifyDamageMultiplicative) => model.ModifyDamageMultiplicative(state, owner, damage),
                        _ => model.ModifyDamageMultiplicativeLate(state, owner, damage),
                    }));
                break;
            case nameof(SimModel.ModifyBlockAdditive) or nameof(SimModel.ModifyBlockMultiplicative):
                foreach (var block in Blocks(owner))
                    yield return ($"{block}", (model, state) => Number(hook == nameof(SimModel.ModifyBlockAdditive) ? model.ModifyBlockAdditive(state, owner, block) : model.ModifyBlockMultiplicative(state, owner, block)));
                break;
            case nameof(SimModel.BeforeCardPlayed) or nameof(SimModel.AfterCardPlayed):
                // A run of plays in turn, each through both hooks (a counter's count, a once-a-turn flag), each step's outcome compared.
                var run = Plays(others).Where(play => play.Owner == owner && play.PlayIndex == 0 && play.Card.Energy == 1).ToList();
                if (run.Count > 0)
                    yield return ("a run of plays in turn", (model, state) => Sequence(state, Enumerable.Range(0, 24).Select(index => run[index * 7 % run.Count]), (branch, play) =>
                    {
                        // As the engine runs a play: BeforeCardPlayed, the play started counted, the play finished counted, AfterCardPlayed.
                        branch.CurrentPlay = play;
                        return model.BeforeCardPlayed(branch, owner, play).Select(before =>
                        {
                            var player = before.MutablePlayer(play.Owner);
                            player.Turn = SimKeys.CountPlay(SimKeys.CountPlay(player.Turn, PlayStage.Started, play.Card.Type, play.Card.Tags), PlayStage.Finished, play.Card.Type, play.Card.Tags);
                            return before;
                        }).SelectMany(after => model.AfterCardPlayed(after, owner, play)).ToList();
                    }));
                foreach (var play in Plays(others))
                {
                    yield return ($"{hook} of {Describe(play)}", (model, state) =>
                    {
                        state.CurrentPlay = play;
                        return Branches(hook == nameof(SimModel.BeforeCardPlayed) ? model.BeforeCardPlayed(state, owner, play) : model.AfterCardPlayed(state, owner, play));
                    });
                    // The play's two hooks in turn, as the engine runs them (what BeforeCardPlayed binds, AfterCardPlayed acts on).
                    yield return ($"both hooks of {Describe(play)}", (model, state) =>
                    {
                        state.CurrentPlay = play;
                        return Branches(model.BeforeCardPlayed(state, owner, play).SelectMany(branch => model.AfterCardPlayed(branch, owner, play)).ToList());
                    });
                }
                break;
            case nameof(SimModel.AfterCardEvent):
                // Twelve of each event in turn (a counter's count, a once-a-turn flag), each step's outcome compared.
                foreach (var cardEvent in Enum.GetValues<CardEvent>().Where(_ => Who.IsPlayer(owner)))
                    yield return ($"twelve {cardEvent} for {owner} in turn", (model, state) => Sequence(state, Enumerable.Repeat(0, 12), (branch, _) => model.AfterCardEvent(branch, owner, cardEvent, owner)));
                foreach (var cardEvent in Enum.GetValues<CardEvent>())
                    foreach (var player in others.Where(Who.IsPlayer))
                        foreach (var card in new[] { null, new SimCard("Skilled", SimCardType.Skill), new SimCard("Attacker", SimCardType.Attack) })
                            foreach (var drawn in new[] { null, new SimDrawCard("status") { Status = true }, new SimDrawCard("ethereal") { Ethereal = true } })
                                yield return ($"{cardEvent} for {player}, card {card?.Type}, drawn {drawn?.Key}", (model, state) =>
                                {
                                    state.EventCard = card;
                                    state.DrawnCard = drawn;
                                    return Branches(model.AfterCardEvent(state, owner, cardEvent, player));
                                });
                break;
            case nameof(SimModel.AfterPowerApplied):
                foreach (var power in new[] { PowerId.Vulnerable, PowerId.Weak, PowerId.Doom, PowerId.Strength, PowerId.Poison, PowerId.DarkShackles, PowerId.Dexterity })
                    foreach (var amount in new[] { 2, -1 })
                        foreach (var applier in others)
                            foreach (var target in new[] { 0, 1, Who.You, Who.Teammate(0) })
                                foreach (var debuff in new[] { true, false })
                                    yield return ($"{power.Name} {amount} by {applier} on {target}, debuff {debuff}", (model, state) =>
                                        Branches(model.AfterPowerApplied(state, owner, power, amount, applier, target, debuff)));
                break;
            case nameof(SimModel.AfterSideTurnEnd):
                yield return ("the players' turn ending", (model, state) => Branches(model.AfterSideTurnEnd(state, owner)));
                break;
        }
    }

    private static IEnumerable<SimCardPlay> Plays(int[] players)
    {
        foreach (var player in players.Where(Who.IsPlayer))
            foreach (var type in Types)
                foreach (var energy in new[] { 0, 1, 2, 3 })
                    foreach (var (index, count) in new[] { (0, 1), (1, 2) })
                    {
                        var card = new SimCard("Played", type) { Energy = energy, Ethereal = energy == 1, Stars = energy == 2 ? 2 : 0, Upgradable = energy == 3 };
                        yield return new SimCardPlay(card, player, Target: 0, index, count, AutoPlay: energy == 3);
                    }
    }

    private static IEnumerable<DamageCtx> Damages(int owner)
    {
        var card = new SimCard("Strike", SimCardType.Attack);
        foreach (var dealer in new int?[] { owner, Who.You, Who.YourOsty, Who.Teammate(0), 0, null }.Distinct())
            foreach (var target in new[] { owner, 1, Who.You, Who.Teammate(0) }.Distinct())
                foreach (var props in new[] { SimProps.Move, SimProps.Move | SimProps.Unpowered, SimProps.Unpowered, SimProps.None, SimProps.Move | SimProps.FromArrow })
                    foreach (var player in new[] { (int?)null, owner, Who.You, Who.Teammate(0) }.Distinct())
                    {
                        var play = player is { } playedBy && Who.IsPlayer(playedBy) ? new SimCardPlay(card, playedBy, target, PlayIndex: 0, PlayCount: 1, AutoPlay: false) : null;
                        yield return new DamageCtx(dealer, target, props, play?.Card, play);
                        if (play == null) yield return new DamageCtx(dealer, target, props, card, Play: null);
                    }
    }

    private static IEnumerable<BlockCtx> Blocks(int owner)
    {
        var card = new SimCard("Defend", SimCardType.Skill);
        foreach (var target in new[] { owner, Who.You, Who.Teammate(0), 0 }.Distinct())
            foreach (var props in new[] { SimProps.Move, SimProps.Move | SimProps.Unpowered, SimProps.Unpowered, SimProps.None })
                foreach (var player in new[] { (int?)null, owner, Who.You, Who.Teammate(0) }.Distinct())
                {
                    var play = player is { } playedBy && Who.IsPlayer(playedBy) ? new SimCardPlay(card, playedBy, target, PlayIndex: 0, PlayCount: 1, AutoPlay: false) : null;
                    yield return new BlockCtx(target, props, play?.Card, play);
                }
    }

    private static string Describe(SimCardPlay play) =>
        $"{play.Card.Type} by {play.Owner} costing {play.Card.Energy}{(play.Card.Ethereal ? " Ethereal" : "")}{(play.Card.Stars > 0 ? $" {play.Card.Stars} stars" : "")} (play {play.PlayIndex + 1} of {play.PlayCount}{(play.AutoPlay ? ", auto" : "")})";

    private static string Number(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A copy without what models keep of their own: every relic entry's counter and flag, every read model's fields.</summary>
    private static SimState Stripped(SimState state)
    {
        var copy = state.Clone();
        foreach (var id in copy.PlayerIds)
        {
            var player = copy.MutablePlayer(id);
            foreach (var relic in player.Relics.Items) player.Relics = player.Relics.With(relic.Name, amount: 0, data: 0);
            player.Fields = NamedCounts.Empty;
        }
        foreach (var enemy in copy.Enemies) enemy.Fields = NamedCounts.Empty;
        return copy;
    }

    /// <summary>Calls in turn, each on every branch the one before left; each step's branches as text.</summary>
    private static string Sequence<TStep>(SimState state, IEnumerable<TStep> steps, Func<SimState, TStep, IEnumerable<SimState>> call)
    {
        IEnumerable<SimState> states = new[] { state };
        var text = new StringBuilder();
        foreach (var step in steps)
        {
            states = states.SelectMany(branch => call(branch, step)).ToList();
            text.Append(Branches(states)).Append(" // ");
        }
        return text.ToString();
    }

    /// <summary>
    /// A hook's branches as text: each one's state key, weight, what it logged and whether it's unknown, in key order. What each kind of model
    /// keeps of its own is left out of the key (a relic's counter and flag on its entry, a read model's fields), as two ways of keeping the same
    /// thing: the sequences compare what they do with it.
    /// </summary>
    private static string Branches(IEnumerable<SimState> states)
    {
        var lines = states.ToList().Select(branch =>
        {
            var text = new StringBuilder();
            text.Append($"key {Stripped(branch).Key():X16} weight {branch.Weight:0.####}");
            text.Append(" fired [" + string.Join(",", branch.Fired.Items.Select(entry => $"{entry.Name}:{entry.Amount}/{entry.Data}")) + "]");
            text.Append(" any [" + string.Join(",", branch.FiredAny.Items.Select(entry => $"{entry.Name}:{entry.Amount}/{entry.Data}")) + "]");
            if (branch.Unmodelled != null) text.Append(" unknown: " + branch.Unmodelled);
            return text.ToString();
        }).Order(StringComparer.Ordinal);
        return string.Join(" | ", lines);
    }
}

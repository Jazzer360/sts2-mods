namespace SpireQoL.Sim;

/// <summary>
/// The players' next turn's start in a projection (decision #306): CombatManager.StartTurn for the players' side, each player's SetupPlayerTurn in
/// it, read from the listeners' own code and run in the game's order and listener order.
/// </summary>
public static partial class SimEngine
{
    /// <summary>The hooks of a player's SetupPlayerTurn, in the order it runs them (Hook.AfterEnergyReset, BeforeHandDraw, AfterPlayerTurnStart).</summary>
    private static readonly (string Hook, Func<SimModel, SimState, int, int, IEnumerable<SimState>> Call)[] SetupPlayerTurnHooks =
    {
        (nameof(SimModel.AfterEnergyReset), (model, state, owner, player) => model.AfterEnergyReset(state, owner, player)),
        (nameof(SimModel.AfterEnergyResetLate), (model, state, owner, player) => model.AfterEnergyResetLate(state, owner, player)),
        (nameof(SimModel.BeforeHandDraw), (model, state, owner, player) => model.BeforeHandDraw(state, owner, player)),
        (nameof(SimModel.BeforeHandDrawLate), (model, state, owner, player) => model.BeforeHandDrawLate(state, owner, player)),
        (nameof(SimModel.AfterPlayerTurnStartEarly), (model, state, owner, player) => model.AfterPlayerTurnStartEarly(state, owner, player)),
        (nameof(SimModel.AfterPlayerTurnStart), (model, state, owner, player) => model.AfterPlayerTurnStart(state, owner, player)),
        (nameof(SimModel.AfterPlayerTurnStartLate), (model, state, owner, player) => model.AfterPlayerTurnStartLate(state, owner, player)),
    };

    /// <summary>
    /// The players' next turn starts in a projection (CombatManager.StartTurn for their side, decision #306), as the game runs it:
    ///  1. It's their side's turn (SimState.SideStarting): a new turn's counts, each player's hand left out (what they'd draw is a roll: no card in
    ///     hand acts as that turn ends, none is Ethereal, none auto-plays, and a draw gives nothing the projection follows: SimState.HandLeftOut).
    ///     A listener with no model whose code acts there makes the state after it unknown (YourTurnStartGaps).
    ///  2. Hook.BeforeSideTurnStart, its participants the living players and their pets.
    ///  3. Each player's block cleared unless a listener's ShouldClearBlock keeps it (Creature.AfterTurnStart: Barricade, Blur).
    ///  4. Each living player's SetupPlayerTurn: Hook.AfterEnergyReset (then Late), Hook.BeforeHandDraw (then Late), the hand's draw (left out),
    ///     Hook.AfterPlayerTurnStart (Early, its own, Late), the hook's player that player.
    ///  5. Hook.AfterSideTurnStart, then Late.
    ///  6. Each player's orbs' turn-start passives (OrbQueue.AfterTurnStart: Plasma's energy).
    /// The deaths these cause are handled after each hook, as the turn's end's are. The enemies' next moves are rolled after
    /// Hook.BeforeSideTurnStart (PrepareForNextTurn: <paramref name="rolled"/>, decision #309).
    /// </summary>
    private static void YourTurnStart(BranchSet branches, Action? rolled = null)
    {
        branches.Do(YourNextTurnStarts);
        Fighting(branches, () =>
        {
            HookStep(branches, nameof(SimModel.BeforeSideTurnStart), (model, owner, branch) => model.BeforeSideTurnStart(branch, owner));
            DeathCheck(branches);
        });
        rolled?.Invoke();
        branches.Do(ClearPlayersBlock);
        var players = branches.States.Count > 0 ? branches.States[0].PlayerIds : Array.Empty<int>();
        foreach (var player in players)
        {
            foreach (var (hook, call) in SetupPlayerTurnHooks)
                Fighting(branches, () =>
                {
                    // SetupPlayerTurn does nothing for a dead player.
                    HookStep(branches, hook, (model, owner, branch) => branch.TakesPart(player) ? call(model, branch, owner, player) : new[] { branch });
                    DeathCheck(branches);
                });
        }
        Fighting(branches, () =>
        {
            HookStep(branches, nameof(SimModel.AfterSideTurnStart), (model, owner, branch) => model.AfterSideTurnStart(branch, owner));
            DeathCheck(branches);
            HookStep(branches, nameof(SimModel.AfterSideTurnStartLate), (model, owner, branch) => model.AfterSideTurnStartLate(branch, owner));
            DeathCheck(branches);
        });
        foreach (var player in players) Fighting(branches, () => OrbPassives(branches, player, OrbTrigger.TurnStart));
        branches.Do(state => state.SideStarting = TurnSide.None);
    }

    /// <summary>
    /// The players' next turn starts (CombatManager.StartTurn's start for their side): it's their side's turn, a new turn's counts, each player's hand
    /// left out (decision #304), and a listener the simulator doesn't follow there makes the state unknown (YourTurnStartGaps).
    /// </summary>
    private static void YourNextTurnStarts(SimState state)
    {
        state.EnemyTurnNow = false;
        state.SideStarting = TurnSide.Players;
        state.HandLeftOut = true;
        state.TurnNumber++;
        state.RoundNumber++;
        TurnCountsStartOver(state);
        foreach (var id in state.PlayerIds)
        {
            var player = state.MutablePlayer(id);
            player.HandCount = player.MaybeSkipped = 0;
            player.TurnEndCards = new();
            player.ExhaustingInHand = new();
            player.Fading = player.HexFading = player.UpgradableAtTurnEnd = 0;
            player.Stampede = player.Simpleton = 0;
            player.AutoHand = player.UnplayableHand = null;
        }
        YourTurnStartGaps(state);
    }

    /// <summary>
    /// Each player's block as their turn starts (Creature.AfterTurnStart: ClearBlock unless a listener's ShouldClearBlock says no; the first turn keeps
    /// it, which a projection never reaches).
    /// </summary>
    private static void ClearPlayersBlock(SimState state)
    {
        foreach (var id in state.PlayerIds)
            if (ClearsBlock(state, id)) state.MutablePlayer(id).Block = 0;
    }

    /// <summary>
    /// A listener on the board with no model whose code acts as the players' next turn starts (SimModels.YourTurnStartGap, decision #306): the
    /// simulator doesn't follow it there, so the state after it is unknown, with why. The players' powers, relics and pets' powers, and each living
    /// enemy's powers.
    /// </summary>
    private static void YourTurnStartGaps(SimState state)
    {
        if (state.Unmodelled != null) return;
        foreach (var id in state.PlayerIds)
        {
            var player = state.Player(id)!;
            foreach (var entry in player.Powers.Items) if (SimModels.YourTurnStartGap(entry.Name.Name, enemyOwner: false) is { } why) { state.Unmodelled = why; return; }
            foreach (var entry in player.Relics.Items) if (SimModels.YourTurnStartGap(entry.Name.Name, enemyOwner: false) is { } why) { state.Unmodelled = why; return; }
            foreach (var kind in PetKinds)
                foreach (var entry in player.PetPowers(kind).Items) if (SimModels.YourTurnStartGap(entry.Name.Name, enemyOwner: false) is { } why) { state.Unmodelled = why; return; }
        }
        foreach (var enemy in state.Enemies)
        {
            if (!enemy.Alive) continue;
            foreach (var entry in enemy.Powers.Items) if (SimModels.YourTurnStartGap(entry.Name.Name, enemyOwner: true) is { } why) { state.Unmodelled = why; return; }
        }
    }
}

using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Diagnostics;

/// <summary>
/// ProcAudit, part: the potion check (decision #313). A potion of yours drunk or thrown is one UsePotionAction in the game's action queue: as it
/// starts, its use is built from the board then (SimAdapter.Potion, from the same code as its preview), and when it finishes the state it left is
/// compared with the simulator's outcomes of that use, as a card play's state after the play is (StateDifferences): only a state no branch gives is
/// reported (`potion`). A skip has a counted reason: the use's search stopped at a cap, a branch it doesn't model (decision #92), the turn ended,
/// a potion that isn't drunk by a choice of yours, a state that couldn't be read.
/// </summary>
public sealed partial class ProcAudit
{
    private sealed class PotionRecord
    {
        public required string Name { get; init; }
        public SimState? Start { get; set; }
        public IReadOnlyDictionary<Creature, int>? Ids { get; set; }
        public SimPlay? Play { get; set; }
        public SimState? After { get; set; }
        public IReadOnlyDictionary<Creature, int>? AfterIds { get; set; }
        /// <summary>Why it isn't checked (counted), or null.</summary>
        public string? Skipped { get; set; }
    }

    private readonly Dictionary<GameAction, PotionRecord> _potions = new(ReferenceEqualityComparer.Instance);

    /// <summary>A potion of yours starting (UsePotionAction): its use built from the board now, through the same code as its preview.</summary>
    private void PotionStarting(UsePotionAction action)
    {
        if (_potions.ContainsKey(action)) return; // resumed after a choice inside it
        var me = action.Player;
        var combat = Sts2State.Combat;
        var potion = me.GetPotionAtSlotIndex((int)action.PotionIndex);
        if (potion == null || combat == null) return;
        var record = new PotionRecord { Name = potion.GetType().Name };
        try
        {
            if (!SimAdapter.Usable(potion)) record.Skipped = "a potion that acts on its own";
            else if (!SimModels.Loaded) record.Skipped = "the models weren't read yet";
            else
            {
                var target = action.TargetId is { } targetId ? combat.Creatures.FirstOrDefault(creature => creature.CombatId == targetId)
                    : potion.TargetType.IsSingleTarget() ? me.Creature : null;
                // Its start as its preview's: with the pile's plays built for a use that auto-plays your draw pile's top (decision #338).
                var aimedAtYou = target == me.Creature;
                record.Start = SimAdapter.Build(me, combat, out var ids, pileAutoPlays: aimedAtYou && SimAdapter.PlaysFromDrawPile(potion, Who.You));
                record.Ids = ids;
                record.Play = SimAdapter.Potion(potion, me, combat, ids, SimAdapter.AimOf(target, me, combat, ids));
            }
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't read a potion's use: {ex.Message}");
            record.Skipped = "couldn't read the potion's use";
        }
        _potions[action] = record;
        action.AfterFinished += OnPotionFinished;
        action.BeforeCancelled += OnPotionCancelled;
    }

    private void OnPotionCancelled(GameAction action)
    {
        using var timing = OverlayHost.TimeHook("proc-audit");
        action.AfterFinished -= OnPotionFinished;
        action.BeforeCancelled -= OnPotionCancelled;
        _potions.Remove(action);
    }

    /// <summary>The potion's action is over: the state it left, read now (nothing else has run), then checked.</summary>
    private void OnPotionFinished(GameAction action)
    {
        using var timing = OverlayHost.TimeHook("proc-audit");
        try
        {
            action.AfterFinished -= OnPotionFinished;
            action.BeforeCancelled -= OnPotionCancelled;
            if (!_potions.Remove(action, out var record)) return;
            var me = Sts2State.Me;
            var combat = Sts2State.Combat;
            if (record.Skipped == null && (me == null || combat == null)) record.Skipped = CheckSkip.FightEnded;
            else if (record.Skipped == null && !CombatManager.Instance.IsOverOrEnding && combat!.CurrentSide != CombatSide.Player) record.Skipped = CheckSkip.TurnEnded;
            else if (record.Skipped == null)
            {
                record.After = SimAdapter.Build(me!, combat!, out var afterIds);
                record.AfterIds = afterIds;
            }
            CheckPotion(record);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[audit] couldn't check a potion: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "audit-potion");
        }
    }

    /// <summary>The state the potion left against its simulated use's outcomes (the closest branch), each difference one issue.</summary>
    private static void CheckPotion(PotionRecord record)
    {
        var issues = new List<string>();
        var skipped = record.Skipped;
        if (skipped == null && (record.Start == null || record.Play == null || record.Ids == null || record.After == null || record.AfterIds == null)) skipped = "not captured";
        List<SimState>? branches = null;
        if (skipped == null)
        {
            SimEngine.ApplyPlayTo(record.Start!, record.Play!, out branches, out var capped);
            if (capped || branches.Count == 0) skipped = "its search stopped at a cap";
            else if (branches.FirstOrDefault(branch => branch.Unmodelled != null) is { } unknown) skipped = $"not modelled: {unknown.Unmodelled}";
        }
        if (skipped != null)
        {
            ModEntry.Log.Detail($"[audit] [potion] {record.Name}: outcome not checked ({skipped})");
            Telemetry.Audits.Skipped(Telemetry.Audits.Potion, skipped, record.Name);
            return;
        }
        var best = branches!.Select(branch => StateDifferences(branch, record.After!, record.Ids!, record.AfterIds!, record.Start!.You, drawn: null, "after potion"))
            .OrderBy(differences => differences.Count).First();
        var note = branches!.Count > 1 ? $" (closest of {branches.Count} branches)" : "";
        issues.AddRange(best.Select(difference => difference + note));
        ModEntry.Log.Detail($"[audit] [potion] {record.Name}: outcome {(issues.Count == 0 ? "matched" : string.Join("; ", issues))}");
        Telemetry.Audits.Done(Telemetry.Audits.Potion, issues.Count == 0, issues.Count == 0 ? record.Name : issues[0].Split(':')[0]);
        if (issues.Count == 0) return;
        var kind = issues[0].Split(':')[0];
        var extra = new Dictionary<string, string>
        {
            ["sim_state"] = record.Start!.ToJson(), ["sim_state_after"] = record.After!.ToJson(),
            ["potion_use"] = string.Join("\n", record.Play!.Body.Select(simAction => simAction.ToString())),
        };
        var tags = new Dictionary<string, string> { ["potion"] = record.Name, ["check"] = kind, ["encounter"] = Sts2State.Combat?.Encounter?.GetType().Name ?? "" };
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("potion", $"Potion outcome: {issues[0]} (drank {record.Name})", new[] { "potion-outcome", record.Name, kind },
            $"Drank {record.Name}.\nIssues: {string.Join("; ", issues)}", extra, tags);
    }
}

namespace SpireQoL.Sim;

/// <summary>
/// A game rule the simulator keeps by hand (decision #209): the simulator's code that follows it, and the game's methods it stands in for
/// (Class.Method, as the decompile names them). A game update that changes one of those methods is a change to review here.
/// </summary>
/// <param name="Rule">What the rule is, in the game's words.</param>
/// <param name="Simulator">The simulator's methods that follow it (Type.Method).</param>
/// <param name="Game">The game's methods it stands in for (Class.Method).</param>
public sealed record HandModelledRule(string Rule, IReadOnlyList<string> Simulator, IReadOnlyList<string> Game);

/// <summary>
/// The method list (decision #195's last step, decision #209): every game method the simulator still follows by hand, so a game update's code
/// diff narrows to what matters. Its engine rules are here, each with the game methods it stands in for; the hand-written models and the
/// adapter's per-class rules are listed from the code by HandModelledTests (catalog), which writes Snapshots/hand-modelled.txt and fails when a
/// SimEngine method is neither a rule's nor plumbing, or when a game method named here is on neither branch.
/// </summary>
public static class HandModelled
{
    /// <summary>The engine's rules, each with the game's methods it stands in for.</summary>
    public static readonly IReadOnlyList<HandModelledRule> Engine = new HandModelledRule[]
    {
        new("A card play: its shape (play count, result location), each play's hooks and its body in its program's order",
            new[] { "SimEngine.RunCardPlay", "SimEngine.PlayOnce", "SimEngine.RunEnchantment", "SimEngine.DecideCardPlay", "SimEngine.ApplyPlay", "SimEngine.ApplyPlayTo", "SimEngine.RunScript", "SimEngine.RunScriptTo", "SimEngine.Count", "SimEngine.CardResolved", "SimEngine.RunFixedHits" },
            new[] { "CardModel.OnPlayWrapper", "EnchantmentModel.OnPlay", "Hook.ModifyCardPlayCount", "Hook.AfterModifyingCardPlayCount", "Hook.ModifyCardPlayResultLocation", "Hook.BeforeCardPlayed", "Hook.AfterCardPlayed", "CardCmd.AutoPlay" }),
        new("A card changed during its play: an upgrade of the card in play makes its later plays its upgraded copy's (read by the adapter from an upgraded copy), a keyword added stays on it",
            new[] { "SimEngine.UpgradeCardInPlay", "SimEngine.KeywordCardInPlay", "SimEngine.WithKeywords" },
            new[] { "CardCmd.Upgrade", "CardModel.UpgradeInternal", "CardModel.FinalizeUpgradeInternal", "CardCmd.ApplyKeyword", "CardModel.AddKeyword" }),
        new("A card in a hand made free this turn: the game's random pick among the cards that cost, what a simulation reads of it (the cheapest card at the turn's end)",
            new[] { "SimEngine.FreeInHand" },
            new[] { "CardModel.SetToFreeThisTurn", "CardModel.CostsEnergyOrStars" }),
        new("Damage to a creature: the damage math, block, the HP-loss pipeline, a death held back, the hit's reactions",
            new[] { "SimEngine.Damage", "SimEngine.ModifiedDamage", "SimEngine.CalcDamage", "SimEngine.PlainDamage", "SimEngine.AnyListensToDamage", "SimEngine.HpLossPipeline", "SimEngine.HpLoss",
                "SimEngine.PlayerDamaged", "SimEngine.LandBlockable", "SimEngine.LandOnPlayer", "SimEngine.LandUnblockable", "SimEngine.LoseHp", "SimEngine.Lose", "SimEngine.PlayHp", "SimEngine.BiggestHit",
                "SimEngine.HitOnce", "SimEngine.HitAll", "SimEngine.HitRandom", "SimEngine.RandomTarget", "SimEngine.ProcHitOn", "SimEngine.ProcDamageAll", "SimEngine.ProcDamageRandom",
                "SimEngine.ProcDamageOn", "SimEngine.ProcDamagePlayer", "SimEngine.DamagePlayer", "SimEngine.RunDamage", "SimEngine.IntentDamage", "SimEngine.WithoutEnchantment", "SimEngine.DamageAllEnemies" },
            new[] { "CreatureCmd.Damage", "Hook.ModifyDamage", "Hook.AfterModifyingDamageAmount", "Hook.BeforeDamageReceived", "Hook.ModifyHpLost", "Hook.AfterModifyingHpLostBeforeOsty",
                "Hook.AfterModifyingHpLostAfterOsty", "Hook.ModifyPreBlockDamageTarget", "Hook.ModifyUnblockedDamageTarget", "Hook.AfterBlockBroken", "Hook.AfterDamageGiven",
                "Hook.AfterDamageReceived", "Hook.ShouldDie", "Hook.AfterPreventingDeath", "Creature.DamageBlockInternal", "Creature.LoseHpInternal" }),
        new("An attack: its hits, their targets (a random one a branch each), the attack's hooks",
            new[] { "SimEngine.RunAttack", "SimEngine.HitCount" },
            new[] { "AttackCommand.Execute", "DamageCmd.Attack", "Hook.BeforeAttack", "Hook.AfterAttack", "Hook.ModifyAttackHitCount" }),
        new("Block: the block math, its hooks, a power's or relic's block, block lost",
            new[] { "SimEngine.BlockOnce", "SimEngine.BlockBaseNow", "SimEngine.CalcBlock", "SimEngine.GainBlock", "SimEngine.ShareBlock", "SimEngine.ProcBlock", "SimEngine.ProcBlockFor", "SimEngine.EnemyBlock", "SimEngine.RunBlock", "SimEngine.StripBlock" },
            new[] { "CreatureCmd.GainBlock", "Hook.ModifyBlock", "Hook.AfterModifyingBlockAmount", "Hook.BeforeBlockGained", "Hook.AfterBlockGained", "CreatureCmd.LoseBlock", "Creature.GainBlockInternal" }),
        new("A power lands: the amount given, Artifact, its instances and copies, the listeners told; a power decremented or removed",
            new[] { "SimEngine.ApplyPower", "SimEngine.ApplyAndTell", "SimEngine.ApplyInHit", "SimEngine.Tell", "SimEngine.RunApplyPower", "SimEngine.ApplyPowerOf", "SimEngine.AmountOf", "SimEngine.MovePowers", "SimEngine.DecrementPower" },
            new[] { "PowerCmd.Apply", "PowerCmd.FindExistingInstanceForStacking", "PowerCmd.Decrement", "PowerCmd.Remove", "PowerCmd.TickDownDuration", "Hook.ModifyPowerAmountGiven",
                "Hook.AfterModifyingPowerAmountGiven", "Hook.BeforePowerAmountChanged", "Hook.AfterPowerAmountChanged", "Creature.ApplyPowerInternal" }),
        new("Draws: a full hand, No Draw, the discard pile shuffled in, the drawn card's reactions",
            new[] { "SimEngine.DrawCards", "SimEngine.DrawOne", "SimEngine.DrawsStopped", "SimEngine.DrawPrevented", "SimEngine.Drawn", "SimEngine.DrawReactions", "SimEngine.DrawsFromPile", "SimEngine.RoomIn", "SimEngine.JoinHand", "SimEngine.RunDrawnCard", "SimDrawPile.Shuffled", "SimDrawPile.ShuffledWithDiscard" },
            new[] { "CardPileCmd.Draw", "CardPileCmd.DrawInternal", "CardPileCmd.Shuffle", "CardPileCmd.ShuffleIfNecessary", "Hook.ShouldDraw", "Hook.AfterPreventingDraw", "Hook.AfterCardDrawn", "Hook.ModifyShuffleOrder", "Hook.AfterShuffle" }),
        new("Cards leaving and joining piles mid-play: taken, discarded, exhausted, made; the card events and their listeners",
            new[] { "SimEngine.RunTakeFromHand", "SimEngine.TakeFromHand", "SimEngine.TakenEvents", "SimEngine.TakenKey", "SimEngine.ZeroedMatching", "SimEngine.JoinsTakes", "SimEngine.RunLeaveHand",
                "SimEngine.RunExhaustFromHand", "SimEngine.ExhaustChosenFromDrawPile", "SimEngine.RunTakeToHand", "SimEngine.TakeToHand", "SimEngine.TakeOne", "SimEngine.Less", "SimEngine.InPile",
                "SimEngine.ProfileIn", "SimEngine.TakenId", "SimEngine.Discarded", "SimEngine.IntoDrawPile", "SimEngine.AddToHand", "SimEngine.CardEvents", "SimEngine.CardEventsOf", "SimEngine.OneCardEvent", "SimEngine.RunCardEvent",
                "SimEngine.FireCardEvent", "SimEngine.CountEvent", "SimEngine.ProcCardEvent", "SimEngine.Raise", "SimEngine.DrainPending" },
            new[] { "CardCmd.Discard", "CardCmd.Exhaust", "CardPileCmd.Add", "CardPileCmd.AddGeneratedCardToCombat", "Hook.AfterCardDiscarded", "Hook.AfterCardExhausted", "Hook.AfterCardChangedPiles",
                "Hook.AfterCardGeneratedForCombat", "PlayerCmd.GainEnergy", "Hook.AfterEnergySpent", "PlayerCmd.GainStars", "Hook.AfterStarsGained", "Hook.AfterStarsSpent" }),
        new("Auto-plays: from the top of the draw pile, from the hand at the turn's end, the cards that play themselves",
            new[] { "SimEngine.AutoPlayFromDraw", "SimEngine.AutoPlayTop", "SimEngine.PlayFromPile", "SimEngine.HellraiserPlays", "SimEngine.InvincibleFromPile", "SimEngine.AutoPlayFromHand", "SimEngine.AutoPostPlay", "SimEngine.AutoPostPlayOf" },
            new[] { "CardPileCmd.AutoPlayFromDrawPile", "CardCmd.AutoPlay", "Hook.BeforeCardAutoPlayed", "Hook.AfterAutoPostPlayPhaseEntered", "HellraiserPower.AfterCardDrawnEarly", "IAmInvincible.AfterAutoPostPlayPhaseEntered" }),
        new("Orbs: the slots, a channel into them (the front one evoked when they're full), an evoke out of them, the passives' order at the turn's end, " +
            "TriggerPassive's count and its loop, a random orb, and the orb hooks' pipelines; an orb's damage and block as its code's commands run them " +
            "(each orb's own Passive, Evoke and values are read from its code, and Focus, Infused Core, Thunder and Gold-Plated Cables from theirs: decision #214)",
            new[] { "SimEngine.FreshOrb", "SimEngine.ChannelOrb", "SimEngine.ChannelKind", "SimEngine.RunOrb", "SimEngine.Evoke", "SimEngine.EvokeLast", "SimEngine.OrbPassives", "SimEngine.ChangeFocus",
                "SimEngine.OrbValue", "SimEngine.OrbValuesAgain", "SimEngine.OrbDamage", "SimEngine.OrbBlock" },
            new[] { "OrbCmd.Channel", "OrbCmd.Evoke", "OrbCmd.EvokeLast", "OrbCmd.EvokeNext", "OrbCmd.Passive", "OrbCmd.AddSlots", "OrbCmd.RemoveSlots", "OrbQueue.BeforeTurnEnd", "OrbModel.GetRandomOrb",
                "OrbModel.TriggerPassive", "OrbModel.ModifyOrbValue", "Hook.ModifyOrbValue", "Hook.AfterOrbChanneled", "Hook.AfterOrbEvoked", "Hook.ModifyOrbPassiveTriggerCount",
                "Hook.AfterModifyingOrbPassiveTriggerCount" }),
        new("Arrows: shot, impaled, knocked loose",
            new[] { "SimEngine.IsImpaled", "SimEngine.RunShoot", "SimEngine.RunImpale", "SimEngine.RunDislodgeAll", "SimEngine.Dislodge", "SimEngine.PlaysStartingImpaled", "SimEngine.TakeArrows" },
            new[] { "ArrowCmd.ShootArrowAtTarget", "ArrowCmd.ImpaleTarget", "ArrowCmd.DislodgeArrow", "ArrowCmd.RetrieveArrow", "Hook.AfterArrowShot", "Hook.AfterArrowImpaled", "Hook.AfterArrowDislodged" }),
        new("Osty: the HP he loses for his player",
            new[] { "SimEngine.OstyLoses" },
            new[] { "Hook.AfterCurrentHpChanged", "Hook.ModifyUnblockedDamageTarget" }),
        new("Poison: Outbreak's trigger of every enemy's poison, and the game's preview of the next tick (the tick itself is PoisonPower's code: decision #201)",
            new[] { "SimEngine.RunModelCommand", "SimEngine.PoisonTick" },
            new[] { "PoisonPower.Trigger", "PoisonPower.CalculateTotalDamageNextTurn" }),
        new("Deaths: the hooks told as each happens (an end-of-turn step's, and what they raise, before the next), a dead enemy's powers gone, the creatures a death adds, a kill, a stun",
            new[] { "SimEngine.AllyDeaths", "SimEngine.DeathCheck", "SimEngine.Spawn", "SimEngine.Stun", "SimEngine.RunExecute" },
            new[] { "CreatureCmd.Kill", "Hook.AfterDeath", "Creature.RemoveAllPowersAfterDeath", "Hook.ShouldPowerBeRemovedOnDeath", "CreatureCmd.Add", "CreatureCmd.Stun" }),
        new("The players' turn's end: its hooks in their places, Ethereal cards, the cards that act in hand, the hand flushed",
            new[] { "SimEngine.EndOfTurn", "SimEngine.Exhausting", "SimEngine.CardsActInHand" },
            new[] { "CombatManager.EndPlayerTurnPhaseOneInternal", "CombatManager.EndPlayerTurnPhaseTwoInternal", "CombatManager.DoTurnEnd", "CombatManager.FlushPlayerHand", "Hook.BeforeSideTurnEnd",
                "Hook.AfterSideTurnEnd", "Hook.ShouldEtherealTrigger", "Hook.ShouldFlush", "Hook.BeforeFlush" }),
        new("The enemies' turn: its start's hooks, block cleared, each move in turn order, the intents read after a play",
            new[] { "SimEngine.EnemyTurnStart", "SimEngine.StartTheEnemiesTurn", "SimEngine.EnemyTurn", "SimEngine.IntentsAfter" },
            new[] { "CombatManager.StartTurn", "CombatManager.ExecuteEnemyTurn", "MonsterModel.PerformMove", "Hook.BeforeSideTurnStart", "Hook.AfterSideTurnStart", "Hook.ShouldClearBlock", "Creature.ClearBlock" }),
        new("A heal: up to max HP",
            new[] { "SimEngine.Heal" },
            new[] { "CreatureCmd.Heal", "Creature.HealInternal" }),
        new("The fight's log: what each event adds, the counts the questions to it read",
            new[] { "SimEngine.Record", "SimEngine.HistoryCount", "SimEngine.RecordCardEvent", "SimEngine.RecordPlay", "SimEngine.DrawnType", "SimEngine.RecordDamageReceived", "SimEngine.TurnCountsStartOver" },
            new[] { "CombatHistory.Add" }),
        new("The listeners: who hears a hook, in the game's order, a dead enemy's powers left out",
            new[] { "SimEngine.Listeners", "SimEngine.ListenersWith", "SimEngine.BuildListeners", "SimEngine.ListenerOrder", "SimEngine.DeadMask", "SimEngine.Listening", "SimEngine.CurrentSets", "SimEngine.SameSets", "SimEngine.AnyModelled", "SimEngine.Hook", "SimEngine.HookStep", "SimEngine.Listens" },
            new[] { "CombatState.IterateHookListeners" }),
    };

    /// <summary>
    /// The adapter's rules for one class by its name, of the kinds no read model covers yet (enchantments, afflictions, potions, orbs: decision
    /// #208's measurement, Snapshots/model-kinds.txt), each with the game methods it stands in for.
    /// </summary>
    public static readonly IReadOnlyList<HandModelledRule> Adapter = new HandModelledRule[]
    {
        new("Swift's draws counted for the cards that come back to your hand (the Return tags' hand size, outside the simulation)",
            new[] { "SimAdapter.SwiftDraws" },
            new[] { "Swift.OnPlay" }),
        new("Enchantments' damage and block math on a replay (the game's own math, called live, for the first play): Momentum, Goopy, Vigorous",
            new[] { "PlayGrowth.Of" },
            new[] { "Momentum.OnPlay", "Goopy.AfterCardPlayed", "Vigorous.AfterCardPlayed", "EnchantmentModel.EnchantDamageAdditive", "EnchantmentModel.EnchantBlockAdditive" }),
        new("Perfect Fit: a shuffle puts its card on top",
            new[] { "SimDrawPile.Shuffled" },
            new[] { "PerfectFit.ModifyShuffleOrder" }),
        new("Afflictions on a card, by kind (SimAffliction): Hexed's Ethereal, Tainted and Galvanized tested by the powers that read them",
            new[] { "SimReaders.Card", "SimAdapter.EtherealOnlyFromHex", "SimEngine.Exhausting" },
            new[] { "Hexed.AfterCardEnteredCombat", "Tainted.CanAfflictCardType" }),
        new("Fairy in a Bottle and Lizard Tail: a death held back, healed from 0",
            new[] { "SimEngine.Lose" },
            new[] { "FairyInABottle.ShouldDie", "FairyInABottle.AfterPreventingDeath", "LizardTail.ShouldDieLate", "LizardTail.AfterPreventingDeath" }),
    };

    /// <summary>
    /// The engine's methods that follow no game rule of their own: the search (branches, merging, the work caps), the state's bookkeeping, the
    /// program's conditions asked of the state, and the outcome's reading.
    /// </summary>
    public static readonly IReadOnlyList<string> Plumbing = new[]
    {
        "SimEngine.Run", "SimEngine.Then", "SimEngine.AnyListens", "SimEngine.OrbLabel", "SimEngine.Merge", "SimEngine.Detached", "SimEngine.Scratch", "SimEngine.Capped", "SimEngine.MarkUnread", "SimEngine.PlayResults", "SimEngine.Breakdown",
        "SimEngine.Title", "SimEngine.Var", "SimEngine.VarValue", "SimEngine.VarTotal", "SimEngine.PowersOf", "SimEngine.SetPowers", "SimEngine.RelicsOf", "SimEngine.SetRelics", "SimEngine.PowerOn",
        "SimEngine.HasPower", "SimEngine.FieldsOf", "SimEngine.SetField", "SimEngine.SetFields", "SimEngine.TurnOf", "SimEngine.TallyFor", "SimEngine.CountOf", "SimEngine.MostOf", "SimEngine.RunWhen",
        "SimEngine.RunAsk", "SimEngine.Holds", "SimEngine.HoldsNow", "SimEngine.Compares", "SimEngine.IsConditionFilter", "SimEngine.HandFilters", "SimEngine.Flattened", "SimEngine.ConditionParts",
    };
}

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
        new("A potion's use (decision #313): it leaves its owner's belt, BeforePotionUsed, its body (its OnUse read from its code), the log, AfterPotionUsed while its owner lives",
            new[] { "SimEngine.RunPotionUse" },
            new[] { "PotionModel.OnUseWrapper", "PotionModel.RemoveBeforeUse", "Hook.BeforePotionUsed", "Hook.AfterPotionUsed" }),
        new("A card play: its shape (play count, result location), each play's hooks and its body in its program's order",
            new[] { "SimEngine.RunCardPlay", "SimEngine.PlayOnce", "SimEngine.RunEnchantment", "SimEngine.DecideCardPlay", "SimEngine.ApplyPlay", "SimEngine.ApplyPlayTo", "SimEngine.RunScript", "SimEngine.RunScriptTo", "SimEngine.Count", "SimEngine.CardResolved", "SimEngine.RunFixedHits" },
            new[] { "CardModel.OnPlayWrapper", "EnchantmentModel.OnPlay", "Hook.ModifyCardPlayCount", "Hook.AfterModifyingCardPlayCount", "Hook.ModifyCardPlayResultLocation", "Hook.BeforeCardPlayed", "Hook.AfterCardPlayed", "CardCmd.AutoPlay" }),
        new("A card changed during its play: an upgrade of the card in play makes its later plays its upgraded copy's (read by the adapter from an upgraded copy), a keyword added stays on it",
            new[] { "SimEngine.UpgradeCardInPlay", "SimEngine.KeywordCardInPlay", "SimEngine.WithKeywords" },
            new[] { "CardCmd.Upgrade", "CardModel.UpgradeInternal", "CardModel.FinalizeUpgradeInternal", "CardCmd.ApplyKeyword", "CardModel.AddKeyword" }),
        new("A card in a hand made free this turn: the game's random pick among the cards that cost, what a simulation reads of it (the cheapest card at the turn's end)",
            new[] { "SimEngine.FreeInHand" },
            new[] { "CardModel.SetToFreeThisTurn", "CardModel.CostsEnergyOrStars" }),
        new("Damage to a creature: the damage math, its results worked out then applied (beta; main tells Before Damage Received first: BranchRules.DamageWorkedOutFirst), block, the HP-loss pipeline, a death held back, the hit's reactions; on a player the same landing for every damage (an enemy's attack, a card's HP cost, a hook's): the creature the redirect hooks name before block and past it (Wuppy's Loyalty, Die For You: read from their code), and its own result told",
            new[] { "SimEngine.Damage", "SimEngine.ModifiedDamage", "SimEngine.CalcDamage", "SimEngine.PlainDamage", "SimEngine.AnyListensToDamage", "SimEngine.HpLossPipeline", "SimEngine.HpLossAmount", "SimEngine.HpLossPhase", "SimEngine.HpLossFor", "SimEngine.TellHpLoss", "SimEngine.ReceivedFirst", "SimEngine.TellReceived", "SimEngine.HpLossOnPlayer",
                "SimEngine.PlayerDamaged", "SimEngine.Lands", "SimEngine.LandBlockable", "SimEngine.LoseHp", "SimEngine.Lose", "SimEngine.AtZero", "SimEngine.PlayHp", "SimEngine.BiggestHit",
                "SimEngine.HitOnce", "SimEngine.HitAll", "SimEngine.HitRandom", "SimEngine.RandomTarget", "SimEngine.LeastHp", "SimEngine.ProcHitOn", "SimEngine.ProcDamageAll", "SimEngine.ProcDamageRandom",
                "SimEngine.ProcDamageOn", "SimEngine.ProcDamagePlayer", "SimEngine.ProcDamageOsty", "SimEngine.DamagePlayer", "SimEngine.RunDamage", "SimEngine.IntentDamage", "SimEngine.WithoutEnchantment", "SimEngine.DamageAllEnemies",
                "SimEngine.RedirectTarget", "SimEngine.PetDamaged", "SimEngine.PetHp" },
            new[] { "CreatureCmd.Damage", "Hook.ModifyDamage", "Hook.AfterModifyingDamageAmount", "Hook.BeforeDamageReceived", "Hook.ModifyHpLost", "Hook.AfterModifyingHpLostBeforeOsty",
                "Hook.AfterModifyingHpLostAfterOsty", "Hook.ModifyPreBlockDamageTarget", "Hook.ModifyUnblockedDamageTarget", "Hook.AfterBlockBroken", "Hook.AfterDamageGiven",
                "Hook.AfterDamageReceived", "Hook.ShouldDie", "Hook.AfterPreventingDeath", "Creature.DamageBlockInternal", "Creature.LoseHpInternal", "CreatureCmd.GetDamageResults", "CreatureCmd.ApplyDamageResults" }),
        new("An attack: its hits, their targets (a random one a branch each), the attack's hooks",
            new[] { "SimEngine.RunAttack", "SimEngine.HitCount", "SimEngine.EnemyAttackHook" },
            new[] { "AttackCommand.Execute", "DamageCmd.Attack", "Hook.BeforeAttack", "Hook.AfterAttack", "Hook.ModifyAttackHitCount" }),
        new("Block: the block math, its hooks, a power's or relic's block, block lost",
            new[] { "SimEngine.BlockOnce", "SimEngine.BlockBaseNow", "SimEngine.CalcBlock", "SimEngine.GainBlock", "SimEngine.ShareBlock", "SimEngine.ProcBlock", "SimEngine.ProcBlockFor", "SimEngine.EnemyBlock", "SimEngine.RunBlock", "SimEngine.StripBlock", "SimEngine.LoseBlock" },
            new[] { "CreatureCmd.GainBlock", "Hook.ModifyBlock", "Hook.AfterModifyingBlockAmount", "Hook.BeforeBlockGained", "Hook.AfterBlockGained", "CreatureCmd.LoseBlock", "Creature.GainBlockInternal" }),
        new("A power lands: the amount given, Artifact, the players' scaling of a new one on an enemy (its class's own scaling read from its code: decision #234), its instances and copies, the listeners told; a power changed, decremented or removed, gone at the amount its class's AllowNegative says, its AfterRemoved told as it goes; a duration tick skipped once for a debuff new on the players' side (SkipNextDurationTick)",
            new[] { "SimEngine.ApplyPower", "SimEngine.AmountGiven", "SimEngine.ApplyAndTell", "SimEngine.ApplyInHit", "SimEngine.Tell", "SimEngine.RunApplyPower", "SimEngine.ApplyPowerOf", "SimEngine.AmountOf", "SimEngine.MovePowers", "SimEngine.DecrementPower", "SimEngine.RemovePower", "SimEngine.TellRemoved", "SimEngine.CanReceivePowers", "SimEngine.NewInstanceTickSkip", "SimEngine.SpendsTickSkip" },
            new[] { "PowerCmd.Apply", "PowerCmd.ModifyAmount", "PowerModel.ShouldRemoveDueToAmount", "PowerModel.ApplyInternal", "PowerCmd.FindExistingInstanceForStacking", "PowerCmd.Decrement", "PowerCmd.Remove", "PowerModel.AfterRemoved", "PowerCmd.TickDownDuration", "Hook.ModifyPowerAmountGiven",
                "Hook.AfterModifyingPowerAmountGiven", "Hook.BeforePowerAmountChanged", "Hook.AfterPowerAmountChanged", "Creature.ApplyPowerInternal", "Creature.CanReceivePowers", "Hook.ShouldAllowHitting" }),
        new("A play's spread of its target's debuffs onto the other enemies (its loops read from the card's code, decision #293): each power whose amount is a debuff (its class's Type, StackType and AllowNegative read from its code), taken where the code takes them, onto every other hittable enemy as PowerCmd applies it",
            new[] { "SimEngine.KeepTargetDebuffs", "SimEngine.SpreadKeptDebuffs" },
            new[] { "PowerModel.GetTypeForAmount" }),
        new("Draws: a full hand, No Draw, the discard pile shuffled in (a card its enchantment puts on top, read from its code), the drawn card's reactions",
            new[] { "SimEngine.DrawCards", "SimEngine.DrawOne", "SimEngine.DrawsStopped", "SimEngine.DrawPrevented", "SimEngine.Drawn", "SimEngine.DrawReactions", "SimEngine.DrawsFromPile", "SimEngine.RoomIn", "SimEngine.JoinHand", "SimEngine.RunDrawnCard", "SimDrawPile.Shuffled", "SimDrawPile.ShuffledWithDiscard" },
            new[] { "CardPileCmd.Draw", "CardPileCmd.DrawInternal", "CardPileCmd.Shuffle", "CardPileCmd.ShuffleIfNecessary", "Hook.ShouldDraw", "Hook.AfterPreventingDraw", "Hook.AfterCardDrawn", "Hook.ModifyShuffleOrder", "Hook.AfterShuffle" }),
        new("Cards leaving and joining piles mid-play: taken, discarded, exhausted, made; the card events and their listeners",
            new[] { "SimEngine.RunTakeFromHand", "SimEngine.TakeFromHand", "SimEngine.TakenEvents", "SimEngine.TakenKey", "SimEngine.ZeroedMatching", "SimEngine.JoinsTakes", "SimEngine.RunLeaveHand",
                "SimEngine.RunExhaustFromHand", "SimEngine.ExhaustChosenFromDrawPile", "SimEngine.RunTakeToHand", "SimEngine.TakeToHand", "SimEngine.TakeOne", "SimEngine.Less", "SimEngine.InPile",
                "SimEngine.ProfileIn", "SimEngine.TakenId", "SimEngine.Discarded", "SimEngine.IntoDrawPile", "SimEngine.MadeIntoDrawPile", "SimEngine.AddToHand", "SimEngine.CardEvents", "SimEngine.CardEventsOf", "SimEngine.OneCardEvent", "SimEngine.RunCardEvent",
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
        new("Arrows: shot, impaled, knocked loose (a shot at a dead target keeps its arrow: ArrowCmd.ShootArrowAtTarget retrieves it)",
            new[] { "SimEngine.IsImpaled", "SimEngine.RunShoot", "SimEngine.RunImpale", "SimEngine.RunDislodgeAll", "SimEngine.Dislodge", "SimEngine.PlaysStartingImpaled", "SimEngine.TakeArrows" },
            new[] { "ArrowCmd.ShootArrowAtTarget", "ArrowCmd.ImpaleTarget", "ArrowCmd.DislodgeArrow", "ArrowCmd.RetrieveArrow", "Hook.AfterArrowShot", "Hook.AfterArrowImpaled", "Hook.AfterArrowDislodged" }),
        new("Osty: the HP he loses for his player, his result in the fight's log and told to the damage hooks (after his player's on beta, before it on main); a summon (his max HP raised and a heal, a revive, a first summon adding him with Die For You, its hooks) and a heal of him",
            new[] { "SimEngine.OstyLoses", "SimEngine.OstyDrops", "SimEngine.OstyId", "SimEngine.TellOstyHit", "SimEngine.RecordOstyHit", "SimEngine.OstyDeath", "SimEngine.PetDied", "SimEngine.Summon", "SimEngine.RunSummon", "SimEngine.HealOsty", "SimEngine.RunHeal" },
            new[] { "Hook.AfterCurrentHpChanged", "Hook.ModifyUnblockedDamageTarget", "Hook.ShouldCreatureBeRemovedFromCombatAfterDeath", "Creature.RemoveAllPowersAfterDeath", "OstyCmd.Summon", "Hook.ModifySummonAmount", "Hook.AfterSummon", "Hook.AfterOstyRevived",
                "CreatureCmd.GainMaxHp", "CreatureCmd.SetMaxHp", "PlayerCmd.AddPet", "CombatHistory.Summoned" }),
        new("Doom's kill (decision #310): each creature of the list the code worked out killed in turn (its deaths' hooks as the engine follows every death), then the listeners told that list; whom it takes is DoomPower's own code, read (KillEachStep, KillEachAction)",
            new[] { "SimEngine.KillEach", "SimEngine.RunKillEach", "SimEngine.TellDiedToDoom" },
            new[] { "DoomPower.DoomKill", "Hook.AfterDiedToDoom" }),
        new("Poison: Outbreak's trigger of every enemy's poison, and the game's preview of the next tick (the tick itself is PoisonPower's code: decision #201)",
            new[] { "SimEngine.RunModelCommand", "SimEngine.PoisonTick" },
            new[] { "PoisonPower.Trigger", "PoisonPower.CalculateTotalDamageNextTurn" }),
        new("Deaths: the hooks told as each happens (an end-of-turn step's, and what they raise, before the next), a dead enemy's powers gone, the creatures a death adds (each made as the game makes it: its HP none an earlier one of the same death joined with), a dead enemy its death's code brings back, a kill, a stun, a move set; the fight ending with no primary enemy alive unless a listener holds it back",
            new[] { "SimEngine.AllyDeaths", "SimEngine.DeathCheck", "SimEngine.Spawn", "SimEngine.SpawnPowers", "SimEngine.Stun", "SimEngine.SetMove", "SimEngine.Revive", "SimEngine.FightEnding", "SimEngine.RunExecute", "SimEngine.KillPlayer" },
            new[] { "CreatureCmd.Kill", "CreatureCmd.KillWithoutCheckingWinCondition", "Hook.AfterDeath", "Creature.RemoveAllPowersAfterDeath", "Hook.ShouldPowerBeRemovedOnDeath", "CreatureCmd.Add", "CreatureCmd.Stun",
                "CombatState.CreateCreature", "Creature.SetUniqueMonsterHpValue", "CreatureCmd.SetMaxAndCurrentHp", "CreatureCmd.SetCurrentHp", "MonsterModel.SetMoveImmediate", "CombatManager.IsCombatEnding", "Hook.ShouldStopCombatFromEnding" }),
        new("The fight's end inside a turn (decision #301): once no primary enemy is alive and nothing holds it back, a branch's later commands, its combat hooks, a card's later plays and the turn's end don't run",
            new[] { "SimEngine.FightOver", "SimEngine.AnyEnding", "SimEngine.Fighting", "SimEngine.WhileFighting", "SimEngine.RunWhileFighting" },
            new[] { "Hook.IterateCombatHookListeners", "CardModel.OnPlayWrapper" }),
        new("The players' turn's end: its hooks in their places, Ethereal cards, the cards that act in hand, the hand flushed",
            new[] { "SimEngine.EndOfTurn", "SimEngine.EndOfTurnSteps", "SimEngine.Exhausting", "SimEngine.CardsActInHand" },
            new[] { "CombatManager.EndPlayerTurnPhaseOneInternal", "CombatManager.EndPlayerTurnPhaseTwoInternal", "CombatManager.DoTurnEnd", "CombatManager.FlushPlayerHand", "Hook.BeforeSideTurnEnd",
                "Hook.AfterSideTurnEnd", "Hook.ShouldEtherealTrigger", "Hook.ShouldFlush", "Hook.BeforeFlush" }),
        new("Who takes an extra turn as the players' turn is over (decision #319): each player asked of every listener, Early first; the first that says yes is the source",
            new[] { "SimEngine.ExtraTurns", "SimEngine.ExtraTurnSource" },
            new[] { "CombatManager.SwitchFromPlayerToEnemySide", "Hook.ShouldTakeExtraTurn" }),
        new("The enemies' turn: its start's hooks, block cleared, each move in turn order, its end's hooks, the intents read after a play",
            new[] { "SimEngine.EnemyTurnStart", "SimEngine.StartTheEnemiesTurn", "SimEngine.EnemyTurn", "SimEngine.EnemyTurnEnd", "SimEngine.EnemyTurnEndSteps", "SimEngine.EnemiesSideStep", "SimEngine.TheirTurnEndUnknownApart", "SimEngine.TheirTurnEndGaps", "SimEngine.TurnEndBlockBack", "SimEngine.ClearsBlock", "SimEngine.IntentsAfter" },
            new[] { "CombatManager.StartTurn", "CombatManager.ExecuteEnemyTurn", "MonsterModel.PerformMove", "Hook.BeforeSideTurnStart", "Hook.AfterSideTurnStart", "Hook.ShouldClearBlock", "Creature.ClearBlock", "CombatManager.EndEnemyTurn", "CombatManager.EndEnemyTurnInternal" }),
        new("The Intent Forecast's later turns (decisions #304, #308): your next turn's start (block cleared, your hand left out) and end with no plays, the enemies' turn start, each enemy's forecast move (a branch per move of a random pick; rolled from its rules on the projected state, decision #309), their turn's end; only what the forecast shows followed (no HP or death), each phase ending on what every outcome shares; each move's hits on you as they land and as its intent reads when your turn starts",
            new[] { "SimEngine.Project", "SimEngine.TheirTurn", "SimEngine.TheirTurnIsOver", "SimEngine.YourNextTurnStarts", "SimEngine.Certainly", "SimEngine.Certain", "SimEngine.CertainSet", "SimEngine.RollChangesItsHits", "SimEngine.HitsOnYou", "SimEngine.HitOnYou", "SimEngine.GiveMoves", "SimEngine.Give", "SimEngine.LandsOnYou", "SimEngine.Landed", "SimEngine.Rolled", "SimEngine.StunsSpent" },
            new[] { "CombatManager.StartTurn", "MonsterModel.PerformMove", "AttackIntent.GetSingleDamage", "Creature.PrepareForNextTurn", "MonsterMoveStateMachine.RollMove", "Creature.StunInternal" }),
        new("Your next turn's start in a projection (decision #306): its listeners' own code in the game's order (Hook.BeforeSideTurnStart, the block cleared, each player's SetupPlayerTurn hooks, Hook.AfterSideTurnStart, the orbs' turn-start passives), its participants the players and their pets; your hand and draws left out; a listener it doesn't follow there unknown",
            new[] { "SimEngine.YourTurnStart", "SimEngine.ClearPlayersBlock", "SimEngine.YourTurnStartGaps" },
            new[] { "CombatManager.StartTurn", "CombatManager.SetupPlayerTurn", "Creature.AfterTurnStart", "Hook.BeforeSideTurnStart", "Hook.AfterEnergyReset", "Hook.BeforeHandDraw", "Hook.AfterPlayerTurnStart", "Hook.AfterSideTurnStart", "OrbQueue.AfterTurnStart" }),
        new("An enemy's next move, rolled as your next turn starts, from its move rules on a simulated state (decision #307): after a stun, the move it named; else what follows the move on screen",
            new[] { "SimEngine.NextMoveAttacks" },
            new[] { "Creature.PrepareForNextTurn", "MonsterModel.RollMove", "MonsterMoveStateMachine.RollMove", "Creature.StunInternal", "MonsterModel.IntendsToAttack" }),
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
        new("Enchantments' damage and block math on a replay (the game's own math, called live, for the first play): Momentum, Goopy, Vigorous",
            new[] { "PlayGrowth.Of" },
            new[] { "Momentum.OnPlay", "Goopy.AfterCardPlayed", "Vigorous.AfterCardPlayed", "EnchantmentModel.EnchantDamageAdditive", "EnchantmentModel.EnchantBlockAdditive" }),
        new("Hexed's Ethereal, granted by Hex while it lasts and gone when its applier dies, read by the affliction's name (the afflictions themselves are read from their code: decision #225)",
            new[] { "SimAdapter.EtherealOnlyFromHex", "SimEngine.Exhausting" },
            new[] { "HexPower.TryModifyKeywordsInCombat", "HexPower.AfterDeath" }),
        new("Fairy in a Bottle and Lizard Tail: a death held back, healed from 0",
            new[] { "SimEngine.Lose", "SimEngine.AtZero" },
            new[] { "FairyInABottle.ShouldDie", "FairyInABottle.AfterPreventingDeath", "LizardTail.ShouldDieLate", "LizardTail.AfterPreventingDeath" }),
    };

    /// <summary>
    /// The engine's methods that follow no game rule of their own: the search (branches, merging, the work caps), the state's bookkeeping, the
    /// program's conditions asked of the state, and the outcome's reading.
    /// </summary>
    public static readonly IReadOnlyList<string> Plumbing = new[]
    {
        "SimEngine.Run", "SimEngine.RunAction", "SimEngine.AnyMayActOnTheEnemiesSide", "SimEngine.Then", "SimEngine.AnyListens", "SimEngine.OrbLabel", "SimEngine.Merge", "SimEngine.Detached", "SimEngine.Scratch", "SimEngine.Capped", "SimEngine.MarkUnread", "SimEngine.PlayResults", "SimEngine.Breakdown",
        "SimEngine.Title", "SimEngine.Var", "SimEngine.VarValue", "SimEngine.VarTotal", "SimEngine.PowersOf", "SimEngine.SetPowers", "SimEngine.RelicsOf", "SimEngine.SetRelics", "SimEngine.PowerOn",
        "SimEngine.HasPower", "SimEngine.FieldsOf", "SimEngine.SetField", "SimEngine.SetFields", "SimEngine.TurnOf", "SimEngine.TallyFor", "SimEngine.CountOf", "SimEngine.MostOf", "SimEngine.RunWhen",
        "SimEngine.RunAsk", "SimEngine.Holds", "SimEngine.HoldsNow", "SimEngine.Compares", "SimEngine.IsConditionFilter", "SimEngine.HandFilters", "SimEngine.Flattened", "SimEngine.ConditionParts",
    };
}

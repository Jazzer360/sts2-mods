namespace SpireQoL.Sim;

public static partial class SimEngine
{
    // ------------------------------------------------------------------ card plays

    /// <summary>
    /// A card play (CardModel.OnPlayWrapper): per play, BeforeCardPlayed, the play started, its body (OnPlay, then the
    /// enchantment's and affliction's), the play finished, AfterCardPlayed. The turn counters follow the game's history
    /// entries (CardPlayStarted after BeforeCardPlayed; CardPlayFinished before AfterCardPlayed).
    /// </summary>
    private static void RunCardPlay(BranchSet branches, CardPlayAction cardPlayAction)
    {
        // Played from your hand: it has left it until it resolves (SimPlayer.PlayingFromHand).
        if (cardPlayAction.FromHand)
            branches.Do(branch => { if (branch.Player(cardPlayAction.Play.Owner) != null) branch.MutablePlayer(cardPlayAction.Play.Owner).PlayingFromHand++; });
        for (var i = 0; i < Math.Max(1, cardPlayAction.Play.PlayCount); i++)
        {
            var play = cardPlayAction.Play with { PlayIndex = i };
            branches.Do(branch => { branch.CurrentPlay = play; branch.PlayCardBlockGains = 0; branch.PlayEvents = CardEventCounts.None; });
            HookStep(branches, nameof(SimModel.BeforeCardPlayed), (model, owner, branch) => model.BeforeCardPlayed(branch, owner, play));
            branches.Do(branch => Count(branch, play, PlayStage.Started));
            foreach (var action in cardPlayAction.Body) Run(branches, action);
            branches.Do(branch => Count(branch, play, PlayStage.Finished));
            HookStep(branches, nameof(SimModel.AfterCardPlayed), (model, owner, branch) => model.AfterCardPlayed(branch, owner, play));
        }
        branches.Do(branch => branch.CurrentPlay = null);
    }

    /// <summary>The play's turn counters on its owner (SimPlayer.Turn): all, by type, by tag.</summary>
    private static void Count(SimState state, SimCardPlay play, PlayStage stage)
    {
        if (state.Player(play.Owner) == null) return;
        var player = state.MutablePlayer(play.Owner);
        player.Turn = SimKeys.CountPlay(player.Turn, stage, play.Card.Type, play.Card.Tags);
    }

    // ------------------------------------------------------------------ attacks

    private static void RunAttack(BranchSet branches, AttackAction at)
    {
        // A shot with no arrow does nothing: no attack, so no hooks either.
        if (at.NeedsArrow && branches.States.All(branch => branch.ArrowAwayAt < 0)) return;
        var dealer = at.ByOsty ? Who.YourOsty : Who.You;
        branches.Do(branch =>
        {
            branch.AttackSerial++;
            branch.CurrentAttack = branch.AttackSerial;
        });
        AttackInfo Info(SimState state) => new(state.CurrentAttack, state.CurrentPlay?.Owner is { } playOwner && !at.ByOsty ? playOwner : dealer, at.Damage.Props, state.CurrentPlay?.Card);
        HookStep(branches, nameof(SimModel.BeforeAttack), (model, owner, branch) => model.BeforeAttack(branch, owner, Info(branch)));
        for (var i = 0; i < at.Hits; i++)
        {
            switch (at.Mode)
            {
                case SimPlay.Target.Single:
                    branches.Each(branch =>
                    {
                        var target = at.TargetId >= 0 ? at.TargetId : branch.CurrentPlay?.Target ?? -1;
                        if (branch.Enemy(target) is not { Alive: true }) return new[] { branch };
                        var states = HitOnce(branch, Info(branch).Dealer, target, at);
                        // Timber: the kill sets off a second attack on every enemy.
                        if (at.OnKill is { } onKill) states = Then(states, copy => copy.Enemy(target) is { Alive: false }
                            ? HitEveryEnemy(copy, Info(copy).Dealer, onKill) : new[] { copy });
                        return states;
                    });
                    break;
                case SimPlay.Target.All:
                    branches.Each(branch => HitAllRound(branch, rounds: 1));
                    break;
                case SimPlay.Target.Random:
                    branches.Each(branch => RandomTarget(branch, (copy, id) => HitOnce(copy, Info(copy).Dealer, id, at)));
                    break;
            }
        }
        HookStep(branches, nameof(SimModel.AfterAttack), (model, owner, branch) => model.AfterAttack(branch, owner, Info(branch)));
        // Sunder: energy once the attack is over, when it killed its target.
        if (at.OnKillEnergy > 0)
            branches.Each(branch => (at.TargetId >= 0 ? at.TargetId : branch.CurrentPlay?.Target ?? -1) is var target && branch.Enemy(target) is { Alive: false }
                ? ProcCardEvent(branch, CardEvent.EnergyGained, at.OnKillEnergy, branch.CurrentPlay?.Owner ?? Who.You, indirect: false) : new[] { branch });
        branches.Do(branch => branch.CurrentAttack = 0);

        // One hit on every hittable enemy; with RepeatPerKill (Echoing Slash), one more round per enemy it killed.
        IEnumerable<SimState> HitAllRound(SimState state, int rounds)
        {
            if (rounds <= 0) return new[] { state };
            var ids = state.Targetable.Select(enemy => enemy.Id).ToList();
            IEnumerable<SimState> states = new[] { state };
            foreach (var id in ids)
            {
                var target = id;
                states = Then(states, branch => branch.Enemy(target) is { Alive: true } ? HitOnce(branch, Info(branch).Dealer, target, at) : new[] { branch });
            }
            return Then(states, branch => HitAllRound(branch, rounds - 1 + (at.RepeatPerKill ? ids.Count(id => branch.Enemy(id) is { Alive: false }) : 0)));
        }
    }

    /// <summary>One random living enemy: a branch per possible target.</summary>
    private static IEnumerable<SimState> RandomTarget(SimState state, Func<SimState, int, IEnumerable<SimState>> then)
    {
        var targets = state.Targetable.Select(enemy => enemy.Id).ToList();
        if (targets.Count == 0) return new[] { state };
        if (targets.Count == 1) return then(state, targets[0]);
        var all = new List<SimState>();
        foreach (var id in targets)
        {
            var branch = state.Clone();
            branch.Weight = state.Weight / targets.Count;
            all.AddRange(then(branch, id));
        }
        return all;
    }

    /// <summary>
    /// One hit of an attack on an enemy: its damage from the pipeline now (the modelled sources' terms and the captured ones), then
    /// the damage itself (SimEngine.Damage: Thorns, block, HP loss, the hooks).
    /// </summary>
    private static IEnumerable<SimState> HitOnce(SimState state, int dealer, int target, AttackAction at)
    {
        if (at.NeedsArrow && state.ArrowAwayAt < 0) return new[] { state };
        var spec = at.Damage;
        var card = state.CurrentPlay?.Card;
        var ctx = new DamageCtx(dealer, target, spec.Props, card, state.CurrentPlay);
        var mods = spec.Fixed ? null : new List<(SimModel Model, int Owner)>();
        var amount = spec.Fixed ? Math.Max(0, (int)spec.BaseFor(target)) : CalcDamage(state, spec.BaseFor(target), ctx, spec.TermsFor(target), mods);
        if (mods != null) foreach (var (model, o) in mods) model.AfterModifyingDamageAmount(state, o, card);
        // The first hit on each enemy, for the damage-formula audit (not after Mighty Wings gave Strength mid-attack: the preview
        // can't know that).
        if (card != null && !at.ByOsty && !state.FirstHits.ContainsKey(target) && !state.Fired.Has(nameof(Models.MightyWings))) state.FirstHits = state.FirstHits.Add(target, amount);
        if (amount <= 0) return new[] { state };
        if (state.Enemy(target) is { Alive: true } enemy) Damage(state, enemy, amount, dealer, spec.Props, card);
        // Rain Shot: the target's damage to every other hittable enemy (only caps apply).
        if (at.Splash) foreach (var other in state.Targetable.Where(simEnemy => simEnemy.Id != target).ToList()) Damage(state, other, amount, dealer, spec.Props, card);
        AllyDeaths(state); // a death runs its hooks as it happens (AfterDeath: Impaled lets its arrows go, Crab Rage, Explosive)
        return DrainPending(state); // what the hit set off where hooks can't branch (an arrow knocked loose: Yah, Twine)
    }

    // ------------------------------------------------------------------ block, powers, HP

    private static void RunBlock(BranchSet branches, GainBlockAction gainBlockAction)
    {
        for (var i = 0; i < gainBlockAction.Times; i++) branches.Each(branch => BlockOnce(branch, gainBlockAction));
    }

    /// <summary>One block gain of the action, through the block pipeline, with what reacts to it.</summary>
    internal static IEnumerable<SimState> BlockOnce(SimState state, GainBlockAction gainBlockAction)
    {
        if (!state.TakesPart(gainBlockAction.To)) return new[] { state };
        var mods = gainBlockAction.Fixed ? null : new List<(SimModel Model, int Owner)>();
        var amount = gainBlockAction.Fixed ? Math.Max(0, (int)gainBlockAction.Base)
            : CalcBlock(state, gainBlockAction.Base, new BlockCtx(gainBlockAction.To, gainBlockAction.Props, state.CurrentPlay?.Card, state.CurrentPlay), gainBlockAction.Captured, mods);
        var states = GainBlock(state, amount, state.EndOfTurnNow && gainBlockAction.To == Who.You && state.Log ? state.CurrentPlay?.Card.Name ?? "block" : null, gainBlockAction.To);
        // Block from a card play this turn (Unmovable counts the ones from earlier plays).
        if (amount > 0 && state.CurrentPlay != null && gainBlockAction.Props.IsCardOrMonsterMove())
            states = states.Select(branch => { branch.PlayCardBlockGains++; if (branch.CurrentPlay!.Owner == Who.You) branch.You.Turn = branch.You.Turn.Add(SimKeys.CardBlock, delta: 1); return branch; }).ToList();
        if (mods != null && amount > 0) states = states.Select(branch => { foreach (var (model, o) in mods) model.AfterModifyingBlockAmount(branch, o, amount, branch.CurrentPlay); return branch; }).ToList();
        return Then(states, branch => Hook(branch, nameof(SimModel.AfterBlockGained), (model, owner, copy) => model.AfterBlockGained(copy, owner, gainBlockAction.To, amount, copy.CurrentPlay?.Card)));
    }

    /// <summary>
    /// PowerCmd.Apply: the amount through ModifyPowerAmountGiven (Snecko Skull...), then Artifact takes a debuff (a charge
    /// each), else the power goes on (poison into the enemy's poison).
    /// </summary>
    internal static bool ApplyPower(SimState state, int applier, int target, PowerId power, int amount, bool debuff)
    {
        if (amount == 0) return false;
        foreach (var (model, o) in Listeners(state)) amount = model.ModifyPowerAmountGiven(state, o, power, amount, applier, target, debuff);
        if (amount == 0) return false;
        var powers = PowersOf(state, target);
        if (debuff && powers[PowerId.Artifact] > 0)
        {
            SetPowers(state, target, powers[PowerId.Artifact] > 1 ? powers.Add(PowerId.Artifact, delta: -1) : powers.Without(PowerId.Artifact));
            return false;
        }
        if (target >= 0 && power == PowerId.Poison)
        {
            state.Enemy(target)?.AddPoison(amount);
            return true;
        }
        // Accelerant on any player: every enemy's poison ticks that many more times (PoisonPower.TriggerCount sums every
        // living player's), and its next tick deals that much more.
        if (Who.IsPlayer(target) && power == PowerId.Accelerant)
            foreach (var enemy in state.Enemies)
            {
                var more = enemy.PoisonTriggers + amount;
                enemy.Poison += PoisonTick(enemy.PoisonStacks, more) - PoisonTick(enemy.PoisonStacks, enemy.PoisonTriggers);
                enemy.PoisonTriggers = more;
            }
        var had = powers.Has(power);
        SetPowers(state, target, powers.Add(power, amount));
        if (!had) SimModels.Get(power.Name)?.AfterApplied(state, target, applier, amount);
        return true;
    }

    /// <summary>The power on each creature it reaches, then AfterPowerApplied for each that landed (Artifact took none of it).</summary>
    private static void RunApplyPower(BranchSet branches, ApplyPowerAction applyPowerAction)
    {
        IEnumerable<SimState> Apply(SimState state, int applier, int target) =>
            ApplyPower(state, applier, target, applyPowerAction.Power, applyPowerAction.CopiesOwn ? PowerOn(state, applier, applyPowerAction.Power) : applyPowerAction.Amount, applyPowerAction.Debuff)
                ? Hook(state, nameof(SimModel.AfterPowerApplied), (model, owner, branch) => model.AfterPowerApplied(branch, owner, applyPowerAction.Power, applyPowerAction.Amount, applier, target, applyPowerAction.Debuff)) : new[] { state };
        if (applyPowerAction.To == ApplyPowerAction.Where.RandomEnemy)
        {
            // Each application to a random hittable enemy: a branch per target, one step each (the branch budget holds).
            for (var i = 0; i < applyPowerAction.Times; i++) branches.Each(branch => RandomTarget(branch, (copy, id) => Apply(copy, branch.CurrentPlay?.Owner ?? Who.You, id)));
            return;
        }
        branches.Each(branch =>
        {
            var applier = branch.CurrentPlay?.Owner ?? Who.You;
            var targets = applyPowerAction.To switch
            {
                ApplyPowerAction.Where.Target => branch.CurrentPlay?.Target is { } aimed && (aimed < 0 || branch.Enemy(aimed) is { Alive: true }) ? new List<int> { aimed } : new List<int>(),
                ApplyPowerAction.Where.AllEnemies => branch.Targetable.Select(enemy => enemy.Id).ToList(),
                ApplyPowerAction.Where.You => new List<int> { applier },
                _ => new List<int> { applyPowerAction.Creature },
            };
            IEnumerable<SimState> states = new[] { branch };
            foreach (var target in targets)
            {
                var aimedAt = target;
                states = Then(states, copy => Apply(copy, applier, aimedAt));
            }
            return states;
        });
    }

    /// <summary>Card events, one card at a time, each through every listener's AfterCardEvent (indirect ones marked in the log).</summary>
    private static void RunCardEvent(BranchSet branches, CardEventAction cardEventAction)
    {
        if (cardEventAction.FirstPlayOnly && branches.States.Count > 0 && branches.States[0].CurrentPlay is { PlayIndex: > 0 }) return;
        for (var i = 0; i < cardEventAction.Count; i++)
        {
            // A draw from your draw pile (decision #90): a branch per card that matters, with its reactions.
            if (cardEventAction.Event == CardEvent.Drawn && branches.States.Count > 0 && DrawsFromPile(branches.States[0], cardEventAction.Player ?? branches.States[0].CurrentPlay?.Owner ?? Who.You))
            {
                branches.Each(branch => DrawCards(branch, cardEventAction.Player ?? branch.CurrentPlay?.Owner ?? Who.You, count: 1, cardEventAction.Indirect));
                continue;
            }
            branches.Do(branch => { CountEvent(branch, cardEventAction.Event, cardEventAction.Player ?? branch.CurrentPlay?.Owner ?? Who.You); branch.EventCard = cardEventAction.Card; });
            HookStep(branches, nameof(SimModel.AfterCardEvent), (model, owner, branch) =>
            {
                branch.IndirectNow = cardEventAction.Indirect;
                var states = model.AfterCardEvent(branch, owner, cardEventAction.Event, cardEventAction.Player ?? branch.CurrentPlay?.Owner ?? Who.You).ToList();
                foreach (var state in states) state.IndirectNow = false;
                return states;
            });
            branches.Do(branch => branch.EventCard = null);
        }
    }

    /// <summary>A relic's or power's value (its DynamicVar) as the adapter read it (SimState.Vars), or the fallback.</summary>
    public static int Var(SimState state, int owner, string model, string var, int fallback) =>
        state.Vars.TryGetValue($"{owner}:{model}:{var}", out var value) ? value : fallback;
}

public static partial class SimEngine
{
    /// <summary>An enemy's hits this turn: its intent's, or the final numbers.</summary>
    internal static int HitCount(SimEnemy enemy) => enemy.Intent?.Count ?? enemy.Hits.Count;

    /// <summary>
    /// One hit of an enemy's intent on a player, through the damage pipeline as it lands (a Move from the enemy: its Strength
    /// and Weak, the player's Vulnerable, Intangible...), with the captured terms of unmodelled sources on that player.
    /// </summary>
    internal static int IntentDamage(SimState state, SimEnemy enemy, int hit, int player)
    {
        var otherHit = enemy.Intent![hit];
        return CalcDamage(state, otherHit.Base, new DamageCtx(enemy.Id, player, SimProps.Move, Card: null, Play: null), otherHit.TermsOn(player));
    }
}

public static partial class SimEngine
{
    /// <summary>Blockable damage to a player (Who.You, Who.Teammate(k)): Thorns hitting back at whoever attacked.</summary>
    internal static void LandOnPlayer(SimState state, string name, int amount, int player) => LandBlockable(state, name, amount, player);
}

public static partial class SimEngine
{
    /// <summary>One hit on every hittable enemy with this damage (Timber's second attack), each through the pipeline.</summary>
    private static IEnumerable<SimState> HitEveryEnemy(SimState state, int dealer, DamageSpec spec)
    {
        var at = new AttackAction(Hits: 1, SimPlay.Target.All, spec);
        IEnumerable<SimState> states = new[] { state };
        foreach (var id in state.Targetable.Select(enemy => enemy.Id).ToList())
        {
            var target = id;
            states = Then(states, branch => branch.Enemy(target) is { Alive: true } ? HitOnce(branch, dealer, target, at) : new[] { branch });
        }
        return states;
    }
}

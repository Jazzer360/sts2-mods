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
        // Played from your hand: it has left it until it resolves (SimPlayer.PlayingFromHand), and no exhaust can take it from there.
        if (cardPlayAction.FromHand)
            branches.Do(branch =>
            {
                if (branch.Player(cardPlayAction.Play.Owner) == null) return;
                var player = branch.MutablePlayer(cardPlayAction.Play.Owner);
                player.PlayingFromHand++;
                if (player.ExhaustingInHand.Any(card => card.HandCard == cardPlayAction.HandCard))
                    player.ExhaustingInHand = player.ExhaustingInHand.Where(card => card.HandCard != cardPlayAction.HandCard).ToList();
            });
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

    /// <summary>
    /// Cards leaving the playing player's hand mid-play (what it discards and exhausts from there): the hand is that much smaller from
    /// now, so a draw after it has their room and what reads the hand at the end of the turn counts what's left (Cloak Clasp, Regret).
    /// The whole hand: what was in it is gone too (its Burns, its Ethereal cards, what Stampede could pick, what acts when exhausted).
    /// Which cards go when it isn't the whole hand isn't kept, but how many is.
    /// </summary>
    private static void RunLeaveHand(SimState state, LeaveHandAction leave)
    {
        var owner = state.CurrentPlay?.Owner ?? Who.You;
        if (state.Player(owner) == null) return;
        var player = state.MutablePlayer(owner);
        var inHand = Math.Max(0, player.HandCount - player.PlayingFromHand);
        player.HandCount -= leave.Whole ? inHand : Math.Min(leave.Count, inHand);
        if (!leave.Whole)
        {
            // The cards it takes are the ones that change nothing the simulation follows (decision #167: a choice inside the play is
            // your later decision): there are enough of them unless the cards it follows in your hand (acting at the end of your turn,
            // acting when exhausted, Ethereal, what Stampede or Simpleton could pick) may be all that's left to take.
            var picks = player.AutoHand == null ? 0 : player.Simpleton > 0 ? player.AutoHand.Count : player.AutoHand.Count(handCard => handCard.IsAttack);
            var followed = player.TurnEndCards.Count + player.ExhaustingInHand.Count + player.Fading + picks;
            if (inHand - followed < Math.Min(leave.Count, inHand))
                state.Unmodelled ??= "it takes cards from your hand, and the cards the simulation follows there may be all it can take";
            return;
        }
        player.TurnEndCards = new();
        player.Fading = player.HexFading = 0;
        player.ExhaustingInHand = new();
        if (player.AutoHand != null) player.AutoHand = new();
    }

    /// <summary>
    /// A play exhausting others: the cards that act when exhausted among the ones it can take (ExhaustFromHandAction) give their
    /// energy (Drum of Battle's AfterCardExhausted: PlayerCmd.GainEnergy). All of them; one card at random, a branch per such card by
    /// its share of the cards in the hand and one where another card went. When you choose (decision #167: a choice inside the play
    /// is your later decision), the option that changes nothing the simulation follows: a card that doesn't act when exhausted, when
    /// the hand has one; with only cards that do, the same whichever you take, or unknown when they differ. Cleanse's pick from
    /// your draw pile leaves it the same way: a plain card when there is one.
    /// </summary>
    private static IEnumerable<SimState> RunExhaustFromHand(SimState state, ExhaustFromHandAction exhaust)
    {
        var owner = state.CurrentPlay?.Owner ?? Who.You;
        if (state.Player(owner) is not { } player) return new[] { state };
        if (exhaust.FromDrawPile) return ExhaustChosenFromDrawPile(state, owner);
        bool Takes(ExhaustingCard card) => (exhaust.Only == SimCardType.None || card.Type == exhaust.Only) && (exhaust.Except == SimCardType.None || card.Type != exhaust.Except);
        var taken = player.ExhaustingInHand.Where(Takes).ToList();
        if (taken.Count == 0) return new[] { state };
        switch (exhaust.Pick)
        {
            case ExhaustPick.Chosen:
            {
                var inHand = player.HandCount - player.PlayingFromHand;
                var filtered = exhaust.Only != SimCardType.None || exhaust.Except != SimCardType.None;
                if (!filtered && inHand > taken.Count) return new[] { state }; // a card that doesn't act when exhausted: yours to take
                if (filtered || taken.Select(card => (card.Energy, card.Type)).Distinct().Count() > 1)
                {
                    state.Unmodelled ??= "you choose the card it exhausts, and every card you could take acts when exhausted, not all alike";
                    return new[] { state };
                }
                // Only cards that act when exhausted, all alike: one of them.
                var mine = state.MutablePlayer(owner);
                mine.ExhaustingInHand = mine.ExhaustingInHand.Where(other => other.HandCard != taken[0].HandCard).ToList();
                return ProcCardEvent(state, CardEvent.EnergyGained, taken[0].Energy, owner, indirect: false);
            }
            case ExhaustPick.All:
            {
                state.MutablePlayer(owner).ExhaustingInHand = player.ExhaustingInHand.Where(card => !Takes(card)).ToList();
                IEnumerable<SimState> states = new[] { state };
                foreach (var card in taken) states = Then(states, branch => ProcCardEvent(branch, CardEvent.EnergyGained, card.Energy, owner, indirect: false));
                return states;
            }
            default:
            {
                // One of the cards in the hand, each equally likely (CombatCardSelection.NextItem); of a kind (Thrash), the count of
                // that kind isn't held: unknown.
                if (exhaust.Only != SimCardType.None || exhaust.Except != SimCardType.None)
                {
                    state.Unmodelled ??= "it exhausts a random card of a kind from your hand, and a card that acts when exhausted (Drum of Battle) is one";
                    return new[] { state };
                }
                var inHand = Math.Max(taken.Count, player.HandCount - player.PlayingFromHand);
                var result = new List<SimState>();
                foreach (var card in taken)
                {
                    var branch = state.Clone();
                    branch.Weight = state.Weight / inHand;
                    var mine = branch.MutablePlayer(owner);
                    mine.ExhaustingInHand = mine.ExhaustingInHand.Where(other => other.HandCard != card.HandCard).ToList();
                    result.AddRange(ProcCardEvent(branch, CardEvent.EnergyGained, card.Energy, owner, indirect: false));
                }
                if (inHand > taken.Count)
                {
                    state.Weight = state.Weight * (inHand - taken.Count) / inHand;
                    result.Add(state); // another card went
                }
                return result;
            }
        }
    }

    /// <summary>
    /// Cleanse: the card you choose from your draw pile is exhausted (decision #167: your later decision): a plain card when the pile
    /// has one, else the same whichever you take (its energy when it acts when exhausted), or unknown when they differ. With no pile
    /// read, only its count is kept.
    /// </summary>
    private static IEnumerable<SimState> ExhaustChosenFromDrawPile(SimState state, int owner)
    {
        if (state.Player(owner)?.Draw is not { Count: > 0 } pile) return new[] { state };
        var profiles = pile.Top.Concat(pile.Rest.Select(group => group.Card)).DistinctBy(card => card.Key).ToList();
        var card = profiles.FirstOrDefault(profile => profile.Plain) ?? (profiles.Count == 1 ? profiles[0] : null);
        if (card == null)
        {
            state.Unmodelled ??= "you choose the card it exhausts from your draw pile, and the ones there differ in what the simulation follows";
            return new[] { state };
        }
        var simPlayer = state.MutablePlayer(owner);
        simPlayer.Draw = pile.Rest.FindIndex(group => group.Card.Key == card.Key) is var restIndex and >= 0
            ? pile.WithoutOneOf(restIndex)
            : pile.WithoutTopAt(pile.Top.FindIndex(top => top.Key == card.Key));
        return card.Exhausting is { } acting ? ProcCardEvent(state, CardEvent.EnergyGained, acting.Energy, owner, indirect: false) : new[] { state };
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
            branch.AttackDealt = 0;
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
                        var target = at.TargetId >= 0 ? at.TargetId : branch.CurrentPlay?.Target ?? Who.None;
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
        // Blossom Shot: Stretch for the damage it dealt (PowerCmd.Apply<StretchPower> after the attack, inside the shot).
        if (at.StretchFromDamage)
            branches.Do(branch =>
            {
                var owner = branch.CurrentPlay?.Owner ?? Who.You;
                if (branch.AttackDealt > 0) ApplyPower(branch, owner, owner, PowerId.Stretch, branch.AttackDealt, debuff: false);
            });
        // Once the attack is over, when it killed its target: what it gives (Sunder's energy, Knockout Blow's stars).
        foreach (var reward in at.KillRewards)
            branches.Each(branch => (at.TargetId >= 0 ? at.TargetId : branch.CurrentPlay?.Target ?? Who.None) is var target && branch.Enemy(target) is { Alive: false }
                ? ProcCardEvent(branch, reward.Event, reward.Count, branch.CurrentPlay?.Owner ?? Who.You, indirect: false) : new[] { branch });
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
        // A replay's damage has what the card gained from its plays before it (decision #160).
        var baseDamage = spec.BaseFor(target) + spec.GrowsEachPlay * (state.CurrentPlay?.PlayIndex ?? 0);
        var terms = spec.TermsFor(target);
        if (spec.EnchantFirstPlayOnly && state.CurrentPlay?.PlayIndex > 0) terms = WithoutEnchantment(terms);
        var amount = spec.Fixed ? Math.Max(0, (int)baseDamage) : ModifiedDamage(state, baseDamage, ctx, terms);
        // The first hit on each enemy, for the damage-formula audit (not after Mighty Wings gave Strength mid-attack: the preview
        // can't know that).
        if (card != null && !at.ByOsty && !state.FirstHits.ContainsKey(target) && !state.Fired.Has(nameof(Models.MightyWings))) state.FirstHits = state.FirstHits.Add(target, amount);
        if (amount <= 0) return new[] { state };
        if (state.Enemy(target) is { Alive: true } enemy) Damage(state, enemy, amount, dealer, spec.Props, card);
        // Rain Shot, Omnislice: the target's damage to every other hittable enemy. Beta: a powered hit, only caps apply. Main (MainRule
        // .SplashIsUnpowered): unpowered (Unpowered | Move), through every damage modifier.
        if (at.Splash)
        {
            var unpowered = state.MainRules.HasFlag(MainRule.SplashIsUnpowered);
            var props = unpowered ? SimProps.Unpowered | SimProps.Move : spec.Props;
            foreach (var other in state.Targetable.Where(simEnemy => simEnemy.Id != target).ToList())
            {
                var splash = unpowered ? ModifiedDamage(state, amount, new DamageCtx(dealer, other.Id, props, card, state.CurrentPlay), captured: null) : amount;
                if (splash > 0) Damage(state, other, splash, dealer, props, card);
            }
        }
        AllyDeaths(state); // a death runs its hooks as it happens (AfterDeath: Impaled lets its arrows go, Crab Rage, Explosive)
        return DrainPending(state); // what the hit set off where hooks can't branch (an arrow knocked loose: Yah, Twine)
    }

    /// <summary>The terms without the card's enchantment: it turned itself off after the play before (Vigorous).</summary>
    private static CapturedTerms WithoutEnchantment(CapturedTerms terms) => terms with { EnchantAdd = 0m, EnchantMul = 1m };

    // ------------------------------------------------------------------ block, powers, HP

    private static void RunBlock(BranchSet branches, GainBlockAction gainBlockAction)
    {
        for (var i = 0; i < gainBlockAction.Times; i++) branches.Each(branch => BlockOnce(branch, gainBlockAction));
    }

    /// <summary>
    /// One block gain of the action (CreatureCmd.GainBlock, its hooks in SimPipelines): the block pipeline (ModifyBlock), then
    /// AfterModifyingBlockAmount for the sources that changed the amount, the gain (Juggernaut, Beacon of Hope), then AfterBlockGained.
    /// </summary>
    internal static IEnumerable<SimState> BlockOnce(SimState state, GainBlockAction gainBlockAction)
    {
        if (!state.TakesPart(gainBlockAction.To)) return new[] { state };
        var mods = gainBlockAction.Fixed ? null : new List<(SimModel Model, int Owner)>();
        // A replay's block has what the card gained from its plays before it (decision #160).
        var baseBlock = gainBlockAction.Base + gainBlockAction.GrowsEachPlay * (state.CurrentPlay?.PlayIndex ?? 0);
        var blockTerms = gainBlockAction.Captured;
        if (blockTerms != null && gainBlockAction.EnchantFirstPlayOnly && state.CurrentPlay?.PlayIndex > 0) blockTerms = WithoutEnchantment(blockTerms);
        var amount = gainBlockAction.Fixed ? Math.Max(0, (int)baseBlock)
            : CalcBlock(state, baseBlock, new BlockCtx(gainBlockAction.To, gainBlockAction.Props, state.CurrentPlay?.Card, state.CurrentPlay), blockTerms, mods);
        // Before the gain (CreatureCmd.GainBlock): what changed the amount hears it first, then the block lands.
        if (mods != null && amount > 0) foreach (var (model, o) in mods) model.AfterModifyingBlockAmount(state, o, amount, state.CurrentPlay);
        var states = GainBlock(state, amount, state.EndOfTurnNow && gainBlockAction.To == Who.You && state.Log ? state.CurrentPlay?.Card.Name ?? "block" : null, gainBlockAction.To);
        // Block from a card play this turn (Unmovable counts the ones from earlier plays).
        if (amount > 0 && state.CurrentPlay != null && gainBlockAction.Props.IsCardOrMonsterMove())
            states = states.Select(branch => { branch.PlayCardBlockGains++; if (branch.CurrentPlay!.Owner == Who.You) branch.You.Turn = branch.You.Turn.Add(SimKeys.CardBlock, delta: 1); return branch; }).ToList();
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
        var powerModel = SimModels.Get(power.Name);
        // Each application of a counter power is a copy of its own, counting from the start (Orbit played twice: two Orbits).
        if (powerModel is Models.CounterPower counter) counter.NewCopy(state, target, amount, had);
        // Each copy of an instanced power is applied on its own (a second Intercept on a covered teammate: a Covered of its own).
        if (!had || powerModel is { Instanced: true }) powerModel?.AfterApplied(state, target, applier, amount);
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
                // The play's target: a living enemy, or a teammate (Ignition, Covered); aimed at no one, no one (never you: Who.None).
                ApplyPowerAction.Where.Target => branch.CurrentPlay?.Target is { } aimed && (Who.KindOf(aimed) == CreatureKind.Teammate || branch.Enemy(aimed) is { Alive: true }) ? new List<int> { aimed } : new List<int>(),
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

    /// <summary>What the adapter adds to a value's key for its total over an instanced power's copies (SimReaders.AddVars).</summary>
    public const string TotalSuffix = ".Total";

    /// <summary>
    /// A value summed over every copy of an instanced power on its owner (PowerInstanceType.Instanced: each Monologue played is a
    /// copy with its own Strength, and each gives its own), as the adapter read it; one copy's (Var) when no total was read.
    /// </summary>
    public static int VarTotal(SimState state, int owner, string model, string var, int fallback) =>
        state.Vars.TryGetValue($"{owner}:{model}:{var}{TotalSuffix}", out var total) ? total : Var(state, owner, model, var, fallback);
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

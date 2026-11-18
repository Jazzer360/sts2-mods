namespace SpireQoL.Sim;

public static partial class SimEngine
{
    // ------------------------------------------------------------------ card plays

    /// <summary>
    /// A card play (CardModel.OnPlayWrapper): per play, BeforeCardPlayed, the play started, its body (OnPlay, then the
    /// enchantment's and affliction's), the play finished, AfterCardPlayed. The turn counters follow the game's history
    /// entries (CardPlayStarted after BeforeCardPlayed; CardPlayFinished before AfterCardPlayed).
    /// </summary>
    private static void RunCardPlay(BranchSet b, CardPlayAction pc)
    {
        for (var i = 0; i < Math.Max(1, pc.Play.PlayCount); i++)
        {
            var play = pc.Play with { PlayIndex = i };
            b.Do(s => { s.CurrentPlay = play; s.PlayCardBlockGains = 0; s.PlayEvents = PowerSet.Empty; s.PlayFromHand = pc.FromHand; });
            HookStep(b, nameof(SimModel.BeforeCardPlayed), (m, o, x) => m.BeforeCardPlayed(x, o, play));
            b.Do(s => Count(s, play, "Started"));
            foreach (var a in pc.Body) Run(b, a);
            b.Do(s => Count(s, play, "Finished"));
            HookStep(b, nameof(SimModel.AfterCardPlayed), (m, o, x) => m.AfterCardPlayed(x, o, play));
        }
        b.Do(s => { s.CurrentPlay = null; s.PlayFromHand = false; });
    }

    /// <summary>The play's turn counters on its owner (SimPlayer.Turn): all, by type, by tag.</summary>
    private static void Count(SimState s, SimCardPlay play, string what)
    {
        var p = play.Owner == Who.You ? s.P : play.Owner <= -2 ? s.MutableTeammate(Who.TeammateIndex(play.Owner)) : null;
        if (p == null) return;
        var t = p.Turn.Add(what, 1).Add($"{what}:{play.Card.Type}", 1);
        foreach (var tag in play.Card.Tags) t = t.Add($"{what}#{tag}", 1);
        p.Turn = t;
    }

    // ------------------------------------------------------------------ attacks

    private static void RunAttack(BranchSet b, AttackAction at)
    {
        // A shot with no arrow does nothing: no attack, so no hooks either.
        if (at.NeedsArrow && b.States.All(s => s.ArrowAwayAt < 0)) return;
        var dealer = at.ByOsty ? Who.YourOsty : Who.You;
        b.Do(s =>
        {
            s.AttackSerial++;
            s.CurrentAttack = s.AttackSerial;
        });
        AttackInfo Info(SimState s) => new(s.CurrentAttack, s.CurrentPlay?.Owner is { } o && !at.ByOsty ? o : dealer, at.Damage.Props, s.CurrentPlay?.Card);
        HookStep(b, nameof(SimModel.BeforeAttack), (m, o, x) => m.BeforeAttack(x, o, Info(x)));
        for (var i = 0; i < at.Hits; i++)
        {
            switch (at.Mode)
            {
                case SimPlay.Target.Single:
                    b.Each(s =>
                    {
                        var target = at.TargetId >= 0 ? at.TargetId : s.CurrentPlay?.Target ?? -1;
                        if (s.Enemy(target) is not { Alive: true }) return new[] { s };
                        var r = HitOnce(s, Info(s).Dealer, target, at);
                        // Timber: the kill sets off a second attack on every enemy.
                        if (at.OnKill is { } onKill) r = Then(r, x => x.Enemy(target) is { Alive: false }
                            ? HitEveryEnemy(x, Info(x).Dealer, onKill) : new[] { x });
                        return r;
                    });
                    break;
                case SimPlay.Target.All:
                    b.Each(s => HitAllRound(s, 1));
                    break;
                case SimPlay.Target.Random:
                    b.Each(s => RandomTarget(s, (x, id) => HitOnce(x, Info(x).Dealer, id, at)));
                    break;
            }
        }
        HookStep(b, nameof(SimModel.AfterAttack), (m, o, x) => m.AfterAttack(x, o, Info(x)));
        b.Do(s => s.CurrentAttack = 0);

        // One hit on every hittable enemy; with RepeatPerKill (Echoing Slash), one more round per enemy it killed.
        IEnumerable<SimState> HitAllRound(SimState s, int rounds)
        {
            if (rounds <= 0) return new[] { s };
            var ids = s.Targetable.Select(e => e.Id).ToList();
            IEnumerable<SimState> r = new[] { s };
            foreach (var id in ids)
            {
                var t = id;
                r = Then(r, x => x.Enemy(t) is { Alive: true } ? HitOnce(x, Info(x).Dealer, t, at) : new[] { x });
            }
            return Then(r, x => HitAllRound(x, rounds - 1 + (at.RepeatPerKill ? ids.Count(id => x.Enemy(id) is { Alive: false }) : 0)));
        }
    }

    /// <summary>One random living enemy: a branch per possible target (none with NoRandomHits; one past the budget).</summary>
    private static IEnumerable<SimState> RandomTarget(SimState s, Func<SimState, int, IEnumerable<SimState>> then)
    {
        if (s.NoRandomHits) return new[] { s };
        var targets = s.Targetable.Select(e => e.Id).ToList();
        if (targets.Count == 0) return new[] { s };
        if (targets.Count == 1 || !Branch(s)) return then(s, targets[0]);
        var all = new List<SimState>();
        foreach (var id in targets)
        {
            var c = s.Clone();
            c.Weight = s.Weight / targets.Count;
            all.AddRange(then(c, id));
        }
        return all;
    }

    /// <summary>
    /// One hit of an attack on an enemy: its damage from the pipeline now (the modelled sources' terms and the captured ones), then
    /// the damage itself (SimEngine.Damage: Thorns, block, HP loss, the hooks).
    /// </summary>
    private static IEnumerable<SimState> HitOnce(SimState s, int dealer, int target, AttackAction at)
    {
        if (at.NeedsArrow && s.ArrowAwayAt < 0) return new[] { s };
        var spec = at.Damage;
        var card = s.CurrentPlay?.Card;
        var ctx = new DamageCtx(dealer, target, spec.Props, card, s.CurrentPlay);
        var mods = spec.Fixed ? null : new List<(SimModel Model, int Owner)>();
        var amount = spec.Fixed ? Math.Max(0, (int)spec.BaseFor(target)) : CalcDamage(s, spec.BaseFor(target), ctx, spec.TermsFor(target), mods);
        if (mods != null) foreach (var (m, o) in mods) m.AfterModifyingDamageAmount(s, o, card);
        // The first hit on each enemy, for the damage-formula audit (not after Mighty Wings gave Strength mid-attack: the preview
        // can't know that).
        if (card != null && !at.ByOsty && !s.FirstHits.Has(target.ToString()) && !s.Fired.Has("MightyWings")) s.FirstHits = s.FirstHits.With(target.ToString(), amount);
        if (amount <= 0) return new[] { s };
        if (s.Enemy(target) is { Alive: true } e) Damage(s, e, amount, dealer, spec.Props, card);
        // Rain Shot: the target's damage to every other hittable enemy (only caps apply).
        if (at.Splash) foreach (var other in s.Targetable.Where(x => x.Id != target).ToList()) Damage(s, other, amount, dealer, spec.Props, card);
        AllyDeaths(s); // a death runs its hooks as it happens (AfterDeath: Impaled lets its arrows go, Crab Rage, Explosive)
        return DrainPending(s); // what the hit set off where hooks can't branch (an arrow knocked loose: Yah, Twine)
    }

    // ------------------------------------------------------------------ block, powers, HP

    private static void RunBlock(BranchSet b, GainBlockAction gb)
    {
        for (var i = 0; i < gb.Times; i++)
        {
            b.Each(s =>
            {
                if (gb.To <= -2 && (Who.TeammateIndex(gb.To) >= s.Teammates.Count || s.Teammates[Who.TeammateIndex(gb.To)].Hp <= 0)) return new[] { s };
                var mods = gb.Fixed ? null : new List<(SimModel Model, int Owner)>();
                var amount = gb.Fixed ? Math.Max(0, (int)gb.Base)
                    : CalcBlock(s, gb.Base, new BlockCtx(gb.To, gb.Props, s.CurrentPlay?.Card, s.CurrentPlay), gb.Captured, mods);
                var who = gb.To == Who.You ? -1 : Who.TeammateIndex(gb.To);
                var r = GainBlock(s, amount, null, who);
                // Block from a card play this turn (Unmovable counts the ones from earlier plays).
                if (amount > 0 && s.CurrentPlay != null && gb.Props.IsCardOrMonsterMove())
                    r = r.Select(x => { x.PlayCardBlockGains++; if (x.CurrentPlay!.Owner == Who.You) x.P.Turn = x.P.Turn.Add("CardBlock", 1); return x; }).ToList();
                if (mods != null && amount > 0) r = r.Select(x => { foreach (var (m, o) in mods) m.AfterModifyingBlockAmount(x, o, amount, x.CurrentPlay); return x; }).ToList();
                return Then(r, x => Hook(x, nameof(SimModel.AfterBlockGained), (m, o, y) => m.AfterBlockGained(y, o, gb.To, amount, y.CurrentPlay?.Card)));
            });
        }
    }

    /// <summary>
    /// PowerCmd.Apply: the amount through ModifyPowerAmountGiven (Snecko Skull...), then Artifact takes a debuff (a charge
    /// each), else the power goes on (poison into the enemy's poison).
    /// </summary>
    internal static bool ApplyPower(SimState s, int applier, int target, string power, int amount, bool debuff)
    {
        if (amount == 0) return false;
        foreach (var (m, o) in Listeners(s)) amount = m.ModifyPowerAmountGiven(s, o, power, amount, applier, target, debuff);
        if (amount == 0) return false;
        var powers = PowersOf(s, target);
        if (debuff && powers["ArtifactPower"] > 0)
        {
            SetPowers(s, target, powers["ArtifactPower"] > 1 ? powers.Add("ArtifactPower", -1) : powers.Without("ArtifactPower"));
            return false;
        }
        if (target >= 0 && power == "PoisonPower")
        {
            s.Enemy(target)?.AddPoison(amount);
            return true;
        }
        // Accelerant on any player: every enemy's poison ticks that many more times (PoisonPower.TriggerCount sums every
        // living player's), and its next tick deals that much more.
        if (Who.IsPlayer(target) && power == "AccelerantPower")
            foreach (var e in s.Enemies)
            {
                var more = e.PoisonTriggers + amount;
                e.Poison += PoisonTick(e.PoisonStacks, more) - PoisonTick(e.PoisonStacks, e.PoisonTriggers);
                e.PoisonTriggers = more;
            }
        var had = powers.Has(power);
        SetPowers(s, target, powers.Add(power, amount));
        if (!had) SimModels.Get(power)?.AfterApplied(s, target, applier, amount);
        return true;
    }

    /// <summary>The power on each creature it reaches, then AfterPowerApplied for each that landed (Artifact took none of it).</summary>
    private static void RunApplyPower(BranchSet b, ApplyPowerAction ap)
    {
        IEnumerable<SimState> Apply(SimState x, int applier, int t) =>
            ApplyPower(x, applier, t, ap.Power, ap.CopiesOwn ? PowerOn(x, applier, ap.Power) : ap.Amount, ap.Debuff)
                ? Hook(x, nameof(SimModel.AfterPowerApplied), (m, o, y) => m.AfterPowerApplied(y, o, ap.Power, ap.Amount, applier, t, ap.Debuff)) : new[] { x };
        if (ap.To == ApplyPowerAction.Where.RandomEnemy)
        {
            // Each application to a random hittable enemy: a branch per target, one step each (the branch budget holds).
            for (var i = 0; i < ap.Times; i++) b.Each(s => RandomTarget(s, (x, id) => Apply(x, s.CurrentPlay?.Owner ?? Who.You, id)));
            return;
        }
        b.Each(s =>
        {
            var applier = s.CurrentPlay?.Owner ?? Who.You;
            var targets = ap.To switch
            {
                ApplyPowerAction.Where.Target => s.CurrentPlay?.Target is { } t && (t < 0 || s.Enemy(t) is { Alive: true }) ? new List<int> { t } : new List<int>(),
                ApplyPowerAction.Where.AllEnemies => s.Targetable.Select(e => e.Id).ToList(),
                ApplyPowerAction.Where.You => new List<int> { applier },
                _ => new List<int> { ap.Creature },
            };
            IEnumerable<SimState> r = new[] { s };
            foreach (var target in targets)
            {
                var t = target;
                r = Then(r, x => Apply(x, applier, t));
            }
            return r;
        });
    }

    /// <summary>Card events, one card at a time, each through every listener's AfterCardEvent (indirect ones marked in the log).</summary>
    private static void RunCardEvent(BranchSet b, CardEventAction ce)
    {
        if (ce.FirstPlayOnly && b.States.Count > 0 && b.States[0].CurrentPlay is { PlayIndex: > 0 }) return;
        for (var i = 0; i < ce.Count; i++)
        {
            // A draw from your draw pile (decision #90): a branch per card that matters, with its reactions.
            if (ce.Event == CardEvent.Drawn && b.States.Count > 0 && DrawsFromPile(b.States[0], ce.Player ?? b.States[0].CurrentPlay?.Owner ?? Who.You))
            {
                b.Each(s => DrawCards(s, ce.Player ?? s.CurrentPlay?.Owner ?? Who.You, 1, ce.Indirect));
                continue;
            }
            b.Do(s => { CountEvent(s, ce.Event, ce.Player ?? s.CurrentPlay?.Owner ?? Who.You); s.EventCard = ce.Card; });
            HookStep(b, nameof(SimModel.AfterCardEvent), (m, o, x) =>
            {
                x.IndirectNow = ce.Indirect;
                var r = m.AfterCardEvent(x, o, ce.Event, ce.Player ?? x.CurrentPlay?.Owner ?? Who.You).ToList();
                foreach (var y in r) y.IndirectNow = false;
                return r;
            });
            b.Do(s => s.EventCard = null);
        }
    }

    /// <summary>A relic's or power's value (its DynamicVar) as the adapter read it (SimState.Vars), or the fallback.</summary>
    public static int Var(SimState s, int owner, string model, string var, int fallback) =>
        s.Vars.TryGetValue($"{owner}:{model}:{var}", out var v) ? v : fallback;
}

public static partial class SimEngine
{
    /// <summary>An enemy's hits this turn: its intent's, or the final numbers.</summary>
    internal static int HitCount(SimEnemy e) => e.Intent?.Count ?? e.Hits.Count;

    /// <summary>
    /// One hit of an enemy's intent on a player, through the damage pipeline as it lands (a Move from the enemy: its Strength
    /// and Weak, the player's Vulnerable, Intangible...), with the captured terms of unmodelled sources on that player.
    /// </summary>
    internal static int IntentDamage(SimState s, SimEnemy e, int hit, int player)
    {
        var h = e.Intent![hit];
        return CalcDamage(s, h.Base, new DamageCtx(e.Id, player, SimProps.Move, null, null), h.TermsOn(player));
    }
}

public static partial class SimEngine
{
    /// <summary>Blockable damage to you during your turn (Thorns hitting back...): your block, the HP-loss modifiers, Inferno.</summary>
    internal static void LandOnYou(SimState s, string name, int amount) => LandBlockable(s, name, amount);

    /// <summary>Blockable damage to teammate k.</summary>
    internal static void LandOnTeammate(SimState s, string name, int amount, int k) => LandBlockable(s, name, amount, k);
}

public static partial class SimEngine
{
    /// <summary>One hit on every hittable enemy with this damage (Timber's second attack), each through the pipeline.</summary>
    private static IEnumerable<SimState> HitEveryEnemy(SimState s, int dealer, DamageSpec spec)
    {
        var at = new AttackAction(1, SimPlay.Target.All, spec);
        IEnumerable<SimState> r = new[] { s };
        foreach (var id in s.Targetable.Select(e => e.Id).ToList())
        {
            var t = id;
            r = Then(r, x => x.Enemy(t) is { Alive: true } ? HitOnce(x, dealer, t, at) : new[] { x });
        }
        return r;
    }
}

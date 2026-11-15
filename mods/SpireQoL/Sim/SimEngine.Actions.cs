namespace SpireQoL.Sim;

public static partial class SimEngine
{
    // ------------------------------------------------------------------ card plays

    /// <summary>
    /// A card play (CardModel.OnPlayWrapper): per play, BeforeCardPlayed, the play started, its body (OnPlay, then the
    /// enchantment's and affliction's), the play finished, AfterCardPlayed. The turn counters follow the game's history
    /// entries (CardPlayStarted after BeforeCardPlayed; CardPlayFinished before AfterCardPlayed).
    /// </summary>
    private static void RunPlayCard(BranchSet b, PlayCardAction pc)
    {
        for (var i = 0; i < Math.Max(1, pc.Play.PlayCount); i++)
        {
            var play = pc.Play with { PlayIndex = i };
            b.Do(s => s.CurrentPlay = play);
            b.Each(s => Hook(s, (m, o, x) => m.BeforeCardPlayed(x, o, play)));
            b.Do(s => Count(s, play, "Started"));
            foreach (var a in pc.Body) Run(b, a);
            b.Do(s => Count(s, play, "Finished"));
            b.Each(s => Hook(s, (m, o, x) => m.AfterCardPlayed(x, o, play)));
        }
        b.Do(s => s.CurrentPlay = null);
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
        var dealer = at.ByOsty ? Who.YourOsty : Who.You;
        b.Do(s =>
        {
            s.AttackSerial++;
            s.CurrentAttack = s.AttackSerial;
        });
        AttackInfo Info(SimState s) => new(s.CurrentAttack, s.CurrentPlay?.Owner is { } o && !at.ByOsty ? o : dealer, at.Damage.Props, s.CurrentPlay?.Card);
        b.Each(s => Hook(s, (m, o, x) => m.BeforeAttack(x, o, Info(x))));
        for (var i = 0; i < at.Hits; i++)
        {
            switch (at.Mode)
            {
                case SimPlay.Target.Single:
                    b.Each(s =>
                    {
                        var target = at.TargetId >= 0 ? at.TargetId : s.CurrentPlay?.Target ?? -1;
                        return s.Enemy(target) is { Alive: true } ? HitOnce(s, Info(s).Dealer, target, at) : new[] { s };
                    });
                    break;
                case SimPlay.Target.All:
                    b.Each(s =>
                    {
                        IEnumerable<SimState> r = new[] { s };
                        foreach (var id in s.Targetable.Select(e => e.Id).ToList())
                        {
                            var t = id;
                            r = Then(r, x => x.Enemy(t) is { Alive: true } ? HitOnce(x, Info(x).Dealer, t, at) : new[] { x });
                        }
                        return r;
                    });
                    break;
                case SimPlay.Target.Random:
                    b.Each(s => RandomTarget(s, (x, id) => HitOnce(x, Info(x).Dealer, id, at)));
                    break;
            }
        }
        b.Each(s => Hook(s, (m, o, x) => m.AfterAttack(x, o, Info(x))));
        b.Do(s => s.CurrentAttack = 0);
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
    /// One hit of an attack on an enemy: its damage from the pipeline now, BeforeDamageReceived (Thorns hits back), the damage
    /// (block, then HP), then AfterDamageGiven and AfterDamageReceived.
    /// </summary>
    private static IEnumerable<SimState> HitOnce(SimState s, int dealer, int target, AttackAction at)
    {
        var spec = at.Damage;
        var card = s.CurrentPlay?.Card;
        var ctx = new DamageCtx(dealer, target, spec.Props, card, s.CurrentPlay);
        var amount = spec.Fixed ? Math.Max(0, (int)spec.BaseFor(target)) : CalcDamage(s, spec.BaseFor(target), ctx, spec.TermsFor(target));
        if (amount <= 0) return new[] { s };
        var before = Hook(s, (m, o, x) => m.BeforeDamageReceived(x, o, ctx, amount));
        return Then(before, x =>
        {
            if (x.Enemy(target) is not { Alive: true } e) return new[] { x };
            var powered = spec.Props.IsPoweredAttack();
            e.HitByCard = true;
            var block = e.Block;
            var hp = e.Hp;
            var lost = Damage(e, amount, powered: powered, minimumHpLoss: dealer == Who.You ? x.P.BootMinimum : 0,
                unblockable: spec.Props.HasFlag(SimProps.Unblockable));
            var blocked = block - e.Block;
            if (powered) BiggestHit(x, blocked, lost, hp);
            if (lost > 0) e.ThroughByCard = true;
            var ev = new DamageEvent(dealer, target, spec.Props, card, Math.Max(0, blocked) + Math.Min(lost, Math.Max(0, hp)), Math.Max(0, blocked), lost, !e.Alive);
            var given = Hook(x, (m, o, y) => m.AfterDamageGiven(y, o, ev));
            return Then(given, y => Hook(y, (m, o, z) => m.AfterDamageReceived(z, o, ev)));
        });
    }

    // ------------------------------------------------------------------ block, powers, HP

    private static void RunBlock(BranchSet b, GainBlockAction gb)
    {
        for (var i = 0; i < gb.Times; i++)
        {
            b.Each(s =>
            {
                if (gb.To <= -2 && (Who.TeammateIndex(gb.To) >= s.Teammates.Count || s.Teammates[Who.TeammateIndex(gb.To)].Hp <= 0)) return new[] { s };
                var amount = gb.Fixed ? Math.Max(0, (int)gb.Base)
                    : CalcBlock(s, gb.Base, new BlockCtx(gb.To, gb.Props, s.CurrentPlay?.Card, s.CurrentPlay), gb.Captured);
                var who = gb.To == Who.You ? -1 : Who.TeammateIndex(gb.To);
                var r = GainBlock(s, amount, null, who);
                return Then(r, x => Hook(x, (m, o, y) => m.AfterBlockGained(y, o, gb.To, amount, y.CurrentPlay?.Card)));
            });
        }
    }

    /// <summary>
    /// PowerCmd.Apply: the amount through ModifyPowerAmountGiven (Snecko Skull...), then Artifact takes a debuff (a charge
    /// each), else the power goes on. Poison and the powers the simulator keeps as numbers go there.
    /// </summary>
    internal static void ApplyPower(SimState s, int applier, int target, string power, int amount, bool debuff)
    {
        if (amount == 0) return;
        foreach (var (m, o) in Listeners(s)) amount = m.ModifyPowerAmountGiven(s, o, power, amount, applier, target);
        if (amount == 0) return;
        var powers = PowersOf(s, target);
        if (debuff && powers["ArtifactPower"] > 0)
        {
            SetPowers(s, target, powers["ArtifactPower"] > 1 ? powers.Add("ArtifactPower", -1) : powers.Without("ArtifactPower"));
            return;
        }
        if (target >= 0 && power == "PoisonPower")
        {
            s.Enemy(target)?.AddPoison(amount);
            return;
        }
        if (target == Who.You)
        {
            switch (power)
            {
                case "PlatingPower": s.P.Plating += amount; return;
                case "JuggernautPower": s.P.Juggernaut += amount; return;
                case "FeelNoPainPower": s.P.FeelNoPain += amount; return;
                case "ThornsPower": s.P.Thorns += amount; return;
                case "FlameBarrierPower": s.P.FlameBarrier += amount; return;
            }
        }
        SetPowers(s, target, powers.Add(power, amount));
    }

    private static void RunApplyPower(BranchSet b, ApplyPowerAction ap)
    {
        b.Do(s =>
        {
            var applier = s.CurrentPlay?.Owner ?? Who.You;
            switch (ap.To)
            {
                case ApplyPowerAction.Where.Target:
                    if (s.CurrentPlay?.Target is { } t && (t < 0 || s.Enemy(t) is { Alive: true })) ApplyPower(s, applier, t, ap.Power, ap.Amount, ap.Debuff);
                    break;
                case ApplyPowerAction.Where.AllEnemies:
                    foreach (var e in s.Targetable.ToList()) ApplyPower(s, applier, e.Id, ap.Power, ap.Amount, ap.Debuff);
                    break;
                case ApplyPowerAction.Where.You:
                    ApplyPower(s, applier, applier, ap.Power, ap.Amount, ap.Debuff);
                    break;
                case ApplyPowerAction.Where.Creature:
                    ApplyPower(s, applier, ap.Creature, ap.Power, ap.Amount, ap.Debuff);
                    break;
            }
        });
    }
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

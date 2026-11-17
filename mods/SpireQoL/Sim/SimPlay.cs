namespace SpireQoL.Sim;

/// <summary>
/// A card play to simulate: the card play and what each play does as actions (Card, Before, Body, After: what the adapter builds),
/// and what the card's leaving your hand changes (HandCard, HandDelta, Draws...). It compiles to a script (SimScript.Of); the engine
/// only runs actions.
///
/// The summary fields below (hits as final numbers, block, poison, procs...) are a shorthand for tests and the single-player
/// golden: each compiles to the action the game would run, with its numbers fixed.
/// </summary>
public sealed class SimPlay
{
    public enum Target { None, Single, All, Random }

    public string Name = "";
    /// <summary>The card play (its hooks run around each play); null: a card named Name, Attack when IsAttack, aimed at TargetId.</summary>
    public SimCardPlay? Card;
    /// <summary>What each play does (OnPlay, then the enchantment's), in order.</summary>
    public List<SimAction> Body = new();
    /// <summary>Actions before the card play (the energy it spends) and after it (its own exhaust, events of the whole play).</summary>
    public List<SimAction> Before = new(), After = new();
    /// <summary>The card in your hand this play is (SimHandCard.Id): playing it takes it out of SimPlayer.AutoHand. 0 = none.</summary>
    public int HandCard;
    /// <summary>Cards it draws (the draw pile it leaves: I Am Invincible on top).</summary>
    public int Draws;
    /// <summary>An auto-play (CardPlay.IsAutoPlay): Pael's Eye doesn't count it as a card you played.</summary>
    public bool AutoPlay;
    /// <summary>Times the card plays (Sneaky reacts to each).</summary>
    public int Plays = 1;
    /// <summary>Your hand's size change (-1 for the card, plus the cards it draws, creates or brings back).</summary>
    public int HandDelta = -1;
    /// <summary>Ethereal cards in hand it plays (one less to exhaust at the end of the turn).</summary>
    public int FadingDelta;
    public bool IsAttack;

    // ---- the shorthand (tests): each compiles to an action, numbers fixed

    public Target HitTarget = Target.None;
    /// <summary>The enemy aimed at (Single hits, poison on the target, Capture Spirit, Judgement).</summary>
    public int TargetId = -1;
    /// <summary>Damage of one hit on each enemy (final numbers).</summary>
    public Dictionary<int, int> HitDamage = new();
    /// <summary>Hits per target (Single/All), or random hits in total (Random).</summary>
    public int Hits;
    /// <summary>Timber: when its hit kills the target, a second attack hits every enemy for this much each.</summary>
    public Dictionary<int, int> OnKillHitAll = new();
    /// <summary>Rain Shot: after its hit on the target, every other hittable enemy takes that hit's damage.</summary>
    public bool SplashFromTarget;
    /// <summary>Osty makes the attack.</summary>
    public bool ByOsty;
    public int BlockAmount;
    public int BlockGains;
    /// <summary>Block its enchantment gains after the card's own (Adroit): this many gains of this amount.</summary>
    public (int Gains, int Amount) EnchantmentBlock;
    /// <summary>A card aimed at a teammate: their index; the card's block (BlockToAlly) and channels (ChannelToAlly) go to them.</summary>
    public int AllyTarget = -1;
    public bool BlockToAlly;
    public bool ChannelToAlly;
    public List<OrbOp> OrbOps = new();
    public int AddPlating, AddJuggernaut, AddFeelNoPain, AddThorns, AddFlameBarrier;
    /// <summary>Damage to you once the card resolves (blockable, unpowered), in order.</summary>
    public List<(string Name, int Amount)> DamageToYou = new();
    /// <summary>HP the card costs you (unblockable, unpowered), HpCostTimes times, before its hits.</summary>
    public int HpCost, HpCostTimes;
    /// <summary>Poison applied to the target (Single) or to every enemy (All).</summary>
    public int Poison;
    public bool PoisonAll;
    /// <summary>Poison applied to a random enemy, RandomPoisonTimes times (Bouncing Flask).</summary>
    public int RandomPoison, RandomPoisonTimes;
    /// <summary>Your Focus changes by this much after its hits.</summary>
    public int FocusDelta;
    /// <summary>Judgement: the target dies if it's alive at or below this HP. 0: none.</summary>
    public int ExecuteAtOrBelow;
    /// <summary>Unblockable, unpowered damage to the target from a card that isn't an attack (Capture Spirit), per enemy id.</summary>
    public Dictionary<int, int> TargetDamage = new();
    /// <summary>It kills your Osty (Sacrifice, Bone Shards).</summary>
    public bool KillsOsty;
    /// <summary>Damage procs it sets off (unpowered, final numbers), in order, after it resolves.</summary>
    public List<ProcHit> Procs = new();
    /// <summary>Block procs it sets off: one entry per block gain.</summary>
    public List<int> ProcBlock = new();
}

/// <summary>Damage given as final numbers (test shorthand for a proc): unpowered, from you, to one enemy, every enemy or a random one.</summary>
public sealed class ProcHit
{
    public SimPlay.Target Spread;
    public int TargetId = -1;
    public Dictionary<int, int> Damage = new();
    public int Times = 1;
}

public sealed class OrbOp
{
    public enum Kind { EvokeFront, EvokeAllTwice, Channel, Slots }

    public Kind Op;
    /// <summary>EvokeFront: how many times the front orb is evoked (removed after the last).</summary>
    public int Times = 1;
    /// <summary>Channel: the new orb (its values as given).</summary>
    public SimOrb? Orb;
    /// <summary>Slots: orb slots gained (up to 10) or lost (orbs past the new capacity are dropped from the back).</summary>
    public int Slots;
}

/// <summary>Damage given as final numbers (a proc in the test shorthand): unpowered, from you, no card; Times times.</summary>
public sealed record FixedHitsAction(SimPlay.Target Spread, int TargetId, IReadOnlyDictionary<int, int> Damage, int Times) : SimAction;

/// <summary>Blockable damage to you from an effect (unpowered; Galvanic-like in the test shorthand).</summary>
public sealed record HitYouAction(string Name, int Amount) : SimAction;

/// <summary>Actions that run on a card play's first play only (the test shorthand gives a replay's totals at once).</summary>
public sealed record FirstPlayOnly(IReadOnlyList<SimAction> Actions) : SimAction;

/// <summary>
/// The card has left its owner's hand (Owner: you, or a teammate's auto-play): their hand's size, the hand Stampede and Simpleton
/// pick from, I Am Invincible's draw pile, Fading, attacks this turn, and Pael's Eye (a card played, not an auto-play, disarms it).
/// </summary>
public sealed record CardResolvedAction(int HandDelta, int HandCard, bool AutoPlay, int Draws, int FadingDelta, bool IsAttack) : SimAction
{
    public int Owner { get; init; } = Who.You;
}

/// <summary>A script: the actions a play (or an auto-play, or a lead-in play) runs.</summary>
public sealed class SimScript
{
    public string Name = "";
    public List<SimAction> Actions = new();

    /// <summary>
    /// A play as its script: the energy it spends, the card play (its actions on every play; the shorthand's on the first, as
    /// totals), what follows it, the card leaving your hand, then the shorthand's procs.
    /// </summary>
    public static SimScript Of(SimPlay p)
    {
        var s = new SimScript { Name = p.Name };
        s.Actions.AddRange(p.Before);
        var play = p.Card ?? new SimCardPlay(new SimCard(p.Name, p.IsAttack ? SimCardType.Attack : SimCardType.Skill), Who.You, p.TargetId, 0,
            Math.Max(1, p.Plays), p.AutoPlay);
        var body = new List<SimAction>(p.Body);
        var shorthand = Shorthand(p);
        if (shorthand.Count > 0) body.Add(new FirstPlayOnly(shorthand));
        s.Actions.Add(new CardPlayAction(play, body));
        s.Actions.AddRange(p.After);
        s.Actions.Add(new CardResolvedAction(p.HandDelta, p.HandCard, p.AutoPlay, p.Draws, p.FadingDelta, p.IsAttack) { Owner = play.Owner });
        if (p.RandomPoison > 0 && p.RandomPoisonTimes > 0)
            s.Actions.Add(new ApplyPowerAction("PoisonPower", p.RandomPoison) { To = ApplyPowerAction.Where.RandomEnemy, Times = p.RandomPoisonTimes, Debuff = true });
        foreach (var proc in p.Procs) s.Actions.Add(new FixedHitsAction(proc.Spread, proc.TargetId, proc.Damage, proc.Times));
        foreach (var amount in p.ProcBlock) s.Actions.Add(new GainBlockAction(1, amount, SimProps.Unpowered) { Fixed = true });
        return s;
    }

    /// <summary>The shorthand's actions, in the order a card does them: HP cost, hits, direct damage and executions, orbs, block, Osty, damage to you, powers and poison, Focus.</summary>
    private static List<SimAction> Shorthand(SimPlay p)
    {
        var a = new List<SimAction>();
        for (var i = 0; i < p.HpCostTimes && p.HpCost > 0; i++) a.Add(new LoseHpAction(p.HpCost));
        if (p.Hits > 0 && p.HitTarget != SimPlay.Target.None)
        {
            a.Add(new AttackAction(p.Hits, p.HitTarget, new DamageSpec(0m, SimProps.Move) { BaseOn = Decimals(p.HitDamage), Fixed = true })
            {
                TargetId = p.HitTarget == SimPlay.Target.Single ? p.TargetId : -1,
                ByOsty = p.ByOsty,
                OnKill = p.OnKillHitAll.Count > 0 ? new DamageSpec(0m, SimProps.Move) { BaseOn = Decimals(p.OnKillHitAll), Fixed = true } : null,
                Splash = p.SplashFromTarget,
            });
        }
        if (p.TargetDamage.Count > 0)
            a.Add(new DamageAction(new DamageSpec(0m, SimProps.Unblockable | SimProps.Unpowered) { BaseOn = Decimals(p.TargetDamage), Fixed = true }));
        if (p.ExecuteAtOrBelow > 0) a.Add(new ExecuteAction(p.ExecuteAtOrBelow));
        foreach (var op in p.OrbOps)
        {
            if (op.Op == OrbOp.Kind.Channel && p.ChannelToAlly && p.AllyTarget < 0) continue; // no teammate to channel into
            a.Add(op.Op switch
            {
                OrbOp.Kind.Channel => new OrbAction(OrbAction.Op.Channel) { Values = op.Orb, To = p.ChannelToAlly ? Who.Teammate(p.AllyTarget) : Who.You },
                OrbOp.Kind.EvokeFront => new OrbAction(OrbAction.Op.EvokeFront) { Times = op.Times },
                OrbOp.Kind.EvokeAllTwice => new OrbAction(OrbAction.Op.EvokeAllTwice),
                _ => new OrbAction(OrbAction.Op.Slots) { Slots = op.Slots },
            });
        }
        // The card's block goes to the targeted teammate (none aimed at: to no one, never to you).
        if (p.BlockGains > 0 && !(p.BlockToAlly && p.AllyTarget < 0))
            a.Add(new GainBlockAction(p.BlockGains, p.BlockAmount, SimProps.Move) { Fixed = true, To = p.BlockToAlly ? Who.Teammate(p.AllyTarget) : Who.You });
        if (p.EnchantmentBlock.Gains > 0) a.Add(new GainBlockAction(p.EnchantmentBlock.Gains, p.EnchantmentBlock.Amount, SimProps.Move) { Fixed = true });
        if (p.KillsOsty) a.Add(new KillOstyAction());
        foreach (var (name, amount) in p.DamageToYou) a.Add(new HitYouAction(name, amount));
        void You(string power, int amount)
        {
            if (amount != 0) a.Add(new ApplyPowerAction(power, amount) { To = ApplyPowerAction.Where.You });
        }
        You("PlatingPower", p.AddPlating);
        You("JuggernautPower", p.AddJuggernaut);
        You("FeelNoPainPower", p.AddFeelNoPain);
        You("ThornsPower", p.AddThorns);
        You("FlameBarrierPower", p.AddFlameBarrier);
        if (p.Poison > 0 && (p.PoisonAll || p.TargetId >= 0))
            a.Add(new ApplyPowerAction("PoisonPower", p.Poison) { To = p.PoisonAll ? ApplyPowerAction.Where.AllEnemies : ApplyPowerAction.Where.Target, Debuff = true });
        if (p.FocusDelta != 0) a.Add(new FocusAction(p.FocusDelta));
        return a;
    }

    private static Dictionary<int, decimal> Decimals(Dictionary<int, int> d) => d.ToDictionary(kv => kv.Key, kv => (decimal)kv.Value);
}

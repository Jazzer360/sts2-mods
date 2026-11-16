using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// Plain combat state for the outcome simulator: no game types, so the simulator can be tested on
/// its own (tests/SpireQoL.Sim.Tests). <see cref="SimAdapter"/> builds it from the live game.
/// Amounts are already through the game's modifiers where the game would apply them (damage per
/// enemy is precomputed per target; block amounts are what you'd gain).
/// </summary>
public enum OrbKind { Lightning, Frost, Dark, Glass, Plasma, Unknown }

public sealed class SimOrb
{
    public OrbKind Kind;
    public int Passive;
    public int Evoke;
    /// <summary>Passive triggers at end of turn (Hook.ModifyOrbPassiveTriggerCount).</summary>
    public int Triggers = 1;

    /// <summary>
    /// Glass only: the stored base value (GlassOrb._passiveVal, starts at 4) and what Focus adds to it.
    /// Passive = max(0, base + bonus); each passive firing lowers the BASE by 1 (not below 0), so a fully
    /// decayed Glass orb still deals your Focus. Evoke = 2 x passive.
    /// </summary>
    public int GlassBase;
    public int GlassBonus;

    public SimOrb Clone() => (SimOrb)MemberwiseClone();
}

public sealed class SimEnemy
{
    public int Id;
    public string Name = "";
    public int Hp;
    public int Block;
    /// <summary>Can be targeted and hit (alive and hittable when the simulation starts).</summary>
    public bool Hittable = true;
    /// <summary>Hardened Shell (HardenedShellPower: amount per turn, Data what's left this turn): HP it can still lose this turn, and per turn (int.MaxValue = none).</summary>
    [JsonIgnore] public int ShellLeft
    {
        get => Powers.Has("HardenedShellPower") ? Powers.DataOf("HardenedShellPower") : int.MaxValue;
        set
        {
            if (!Powers.Has("HardenedShellPower") && value == int.MaxValue) return; // no shell: nothing to keep
            Powers = Powers.With("HardenedShellPower", Powers.Has("HardenedShellPower") ? Powers["HardenedShellPower"] : int.MaxValue, value);
        }
    }
    [JsonIgnore] public int ShellPerTurn
    {
        get => Powers.Has("HardenedShellPower") ? Powers["HardenedShellPower"] : int.MaxValue;
        set => Powers = value == int.MaxValue ? Powers.Without("HardenedShellPower")
            : Powers.With("HardenedShellPower", value, Powers.Has("HardenedShellPower") ? Powers.DataOf("HardenedShellPower") : value);
    }
    /// <summary>Damage of its poison tick at the start of its turn.</summary>
    public int Poison;
    /// <summary>Keeps its block between turns (BarricadePower, or BurrowedPower.ShouldClearBlock).</summary>
    [JsonIgnore] public bool Barricade { get => Powers.Has("BarricadePower") || Powers.Has("BurrowedPower"); set => Set("BarricadePower", value ? 1 : 0); }
    public bool Stunned;
    /// <summary>Its attack this turn: damage of each hit on you (the intent's numbers).</summary>
    public List<int> Hits = new();
    /// <summary>
    /// Its attack as pipeline inputs (docs/SIMULATOR_V2.md): each hit's base and the captured terms of unmodelled sources on each
    /// player, so Weak, Strength and the rest are applied as the hit lands. Null: Hits holds the final numbers.
    /// </summary>
    public List<IntentHit>? Intent;
    /// <summary>
    /// Each hit's amount on teammate k (their own Vulnerable, Intangible, Tank...: the game's damage math per
    /// target); missing: the same as yours.
    /// </summary>
    public List<List<int>> TeammateHits = new();
    public bool Imbalanced;
    /// <summary>Most damage one instance can deal it: Hard to Kill's cap, 1 while Intangible (int.MaxValue = none). Setting it sets Hard to Kill.</summary>
    [JsonIgnore] public int HitCap
    {
        get => Powers.Has("IntangiblePower") ? 1 : Powers.Has("HardToKillPower") ? Powers["HardToKillPower"] : int.MaxValue;
        set => Set("HardToKillPower", value == int.MaxValue ? 0 : value);
    }

    // ---- per-branch results
    /// <summary>Damage instances that got through its block during your turn (incl. end of turn).</summary>
    public int UnblockedInstances;
    /// <summary>Its first hit on you was fully blocked (Imbalanced).</summary>
    public bool FirstHitFullyBlocked;
    /// <summary>Dead before it could act (your turn, your end of turn, or its poison tick).</summary>
    public bool DiedBeforeActing;

    /// <summary>Poison stacks and how many times they tick (Accelerant): Poison above is their damage.</summary>
    public int PoisonStacks;
    public int PoisonTriggers = 1;

    // Reactions to your card plays (block it gains during your turn; decompile v0.111.0).
    /// <summary>Curl Up: block it gains after a card whose attack damaged it (once; the power goes).</summary>
    [JsonIgnore] public int CurlUp { get => Powers["CurlUpPower"]; set => Set("CurlUpPower", value); }
    /// <summary>Sneaky: block it gains after each Attack you play.</summary>
    [JsonIgnore] public int Sneaky { get => Powers["SneakyPower"]; set => Set("SneakyPower", value); }
    /// <summary>Skittish: block it gains after a card's attack gets damage through its block (once per turn; 0 once used).</summary>
    [JsonIgnore] public int Skittish { get => Powers["SkittishPower"]; set => Set("SkittishPower", value); }
    /// <summary>It gained block from Curl Up, Sneaky or Skittish because of the play.</summary>
    public bool ReactedWithBlock;

    // Being hit (decompile v0.111.0):
    /// <summary>
    /// Thorns: damage to you (blockable) before each hit of your card's attack on it
    /// (ThornsPower.BeforeDamageReceived on powered attacks), after the game's damage math (0 = none).
    /// </summary>
    [JsonIgnore] public int Thorns { get => Powers["ThornsPower"]; set => Set("ThornsPower", value); }
    /// <summary>Burrowed: keeps its block between turns; breaking that block stuns it (AfterBlockBroken), and the power and its block go.</summary>
    [JsonIgnore] public bool Burrowed { get => Powers.Has("BurrowedPower"); set => Set("BurrowedPower", value ? 1 : 0); }
    /// <summary>Asleep (Lagavulin Matriarch): any damage through its block wakes and stuns it (AsleepPower.AfterDamageReceived).</summary>
    [JsonIgnore] public bool Asleep { get => Powers.Has("AsleepPower"); set => Set("AsleepPower", value ? 1 : 0); }
    /// <summary>
    /// Slumber (Slumbering Beetle): each damage instance through its block takes a stack; the last one wakes it, stunned
    /// (SlumberPower.AfterDamageReceived). 0: none.
    /// </summary>
    [JsonIgnore] public int Slumber { get => Powers["SlumberPower"]; set => Set("SlumberPower", value); }
    /// <summary>Flutter: powered-attack hits with damage through its block left before it's stunned (0 = none).</summary>
    [JsonIgnore] public int Flutter { get => Powers["FlutterPower"]; set => Set("FlutterPower", value); }
    /// <summary>Plow / Shriek: stunned when damage gets through and leaves it at or below this HP (0 = none).</summary>
    [JsonIgnore] public int StunAtHp { get => Math.Max(Powers["PlowPower"], Powers["ShriekPower"]); set => Set("PlowPower", value); }
    /// <summary>The Gambit: dies when a powered attack gets damage through.</summary>
    [JsonIgnore] public bool Gambit { get => Powers.Has("TheGambitPower"); set => Set("TheGambitPower", value ? 1 : 0); }
    public bool GambitTriggered;
    /// <summary>Slippery: hits that lose it at most 1 HP each, left (0 = none).</summary>
    [JsonIgnore] public int Slippery { get => Powers["SlipperyPower"]; set => Set("SlipperyPower", value); }
    public bool SlipperyTriggered;
    /// <summary>Alive when the simulation starts (deaths after that set off Crab Rage and Ravenous).</summary>
    public bool StartedAlive = true;
    /// <summary>Ravenous: an ally dying stuns it (RavenousPower.AfterDeath).</summary>
    [JsonIgnore] public bool Ravenous { get => Powers.Has("RavenousPower"); set => Set("RavenousPower", value ? 1 : 0); }
    /// <summary>Crab Rage: its hits once an ally has died (its Strength; null = no Crab Rage).</summary>
    public List<int>? HitsAfterAllyDies;
    /// <summary>Crab Rage: block it gains when an ally dies.</summary>
    public int CrabRageBlock;
    /// <summary>
    /// Explosive: when it dies, this much damage to you (unpowered, blockable: ExplosivePower.AfterDeath hits every
    /// living player), after the game's damage math for you; 0 = none.
    /// </summary>
    public int Explosive;
    /// <summary>Explosive's damage on teammate k (their own damage math); missing: the same as yours.</summary>
    public List<int>? ExplosiveOnTeammates;
    /// <summary>Its Explosive has gone off (it died during the simulation).</summary>
    public bool Exploded;

    /// <summary>Its death has been handled (AfterDeath dispatched once).</summary>
    public bool DeathHandled;

    /// <summary>Its block was broken (taken from above 0 to 0 by damage) during your turn.</summary>
    /// <summary>Its powers the simulator reads or changes (docs/SIMULATOR_V2.md), in the order they were applied.</summary>
    public PowerSet Powers = PowerSet.Empty;

    /// <summary>Sets a power's amount (0 removes it).</summary>
    private void Set(string power, int amount) => Powers = amount == 0 ? Powers.Without(power) : Powers.With(power, amount);

    public bool BlockBroken;
    /// <summary>Stunned by something during your turn (not at the start).</summary>
    public bool StunnedThisTurn;

    /// <summary>
    /// Adds poison stacks and raises the tick damage by what they add: each tick deals the stacks left,
    /// for min(stacks, triggers) ticks (PoisonPower.CalculateTotalDamageNextTurn, without damage modifiers).
    /// </summary>
    public void AddPoison(int stacks)
    {
        if (stacks <= 0) return;
        int Tick(int a)
        {
            var sum = 0;
            for (var i = 0; i < Math.Min(a, Math.Max(1, PoisonTriggers)); i++) sum += a - i;
            return sum;
        }
        Poison += Tick(PoisonStacks + stacks) - Tick(PoisonStacks);
        PoisonStacks += stacks;
    }

    /// <summary>Its HP when your end of turn is over (before its turn), for the outcome audit.</summary>
    public int HpAtTurnEnd;
    public bool Attacked;

    [JsonIgnore]
    public bool Alive => Hp > 0;

    /// <summary>Copies the numbers; the attack list never changes during a run, so it's shared.</summary>
    public SimEnemy Clone() => (SimEnemy)MemberwiseClone();
}

public sealed class SimPlayer
{
    public int Block;

    private void Set(string power, int amount) => Powers = amount == 0 ? Powers.Without(power) : Powers.With(power, amount);
    private void SetRelic(string relic, int amount) => Relics = amount == 0 ? Relics.Without(relic) : Relics.With(relic, amount);

    /// <summary>Their powers the simulator reads or changes, in the order they were applied (docs/SIMULATOR_V2.md).</summary>
    public PowerSet Powers = PowerSet.Empty;
    /// <summary>Their relics the simulator models, in relic order, with each one's counter (Amount) and flag (Data).</summary>
    public PowerSet Relics = PowerSet.Empty;
    /// <summary>
    /// What they played this turn, as the hooks count it (the game's CardPlaysStarted and CardPlaysFinished): "Started", "Finished",
    /// "Started:Attack", "Finished:Skill", "Finished#Shiv" (by tag)... Each play of a replay counts.
    /// </summary>
    public PowerSet Turn = PowerSet.Empty;

    // Powers that react or act at end of turn.
    [JsonIgnore] public int Juggernaut { get => Powers["JuggernautPower"]; set => Set("JuggernautPower", value); }
    [JsonIgnore] public int Plating { get => Powers["PlatingPower"]; set => Set("PlatingPower", value); }
    [JsonIgnore] public int FeelNoPain { get => Powers["FeelNoPainPower"]; set => Set("FeelNoPainPower", value); }
    // End-of-turn powers and relics, as views onto their entries (the models read the entries: Sim/Models/EndOfTurnModels.cs).
    /// <summary>Hailstorm (amount; Data = the Frost orbs it needs).</summary>
    [JsonIgnore] public int Hailstorm { get => Powers["HailstormPower"]; set => Powers = value == 0 ? Powers.Without("HailstormPower") : Powers.With("HailstormPower", value, Powers.Has("HailstormPower") ? Powers.DataOf("HailstormPower") : 1); }
    [JsonIgnore] public int HailstormFrostNeeded { get => Powers.Has("HailstormPower") ? Powers.DataOf("HailstormPower") : 1; set => Powers = Powers.With("HailstormPower", Powers["HailstormPower"], value); }
    /// <summary>The Bomb's damage when it goes off this turn end (its last tick: amount 1; Data = its damage), else 0.</summary>
    [JsonIgnore] public int TheBombDamage { get => Powers.Has("TheBombPower") && Powers["TheBombPower"] <= 1 ? Powers.DataOf("TheBombPower") : 0; set => Powers = value == 0 ? Powers.Without("TheBombPower") : Powers.With("TheBombPower", 1, value); }
    [JsonIgnore] public int ConsumingShadow { get => Powers["ConsumingShadowPower"]; set => Set("ConsumingShadowPower", value); }
    [JsonIgnore] public int Thorns { get => Powers["ThornsPower"]; set => Set("ThornsPower", value); }
    [JsonIgnore] public int FlameBarrier { get => Powers["FlameBarrierPower"]; set => Set("FlameBarrierPower", value); }
    public bool Reflect;
    /// <summary>
    /// Inferno: each time you lose HP on your turn, this much damage to every enemy (InfernoPower.AfterDamageReceived:
    /// UnblockedDamage &gt; 0 while it's your side's turn; unpowered). Yours only: a teammate's isn't modelled.
    /// </summary>
    [JsonIgnore] public int Inferno { get => Powers["InfernoPower"]; set => Set("InfernoPower", value); }

    // Relics (amount: their block or damage; Data where said).
    [JsonIgnore] public int OrichalcumBlock { get => Relics["Orichalcum"]; set => SetRelic("Orichalcum", value); }
    [JsonIgnore] public int CloakClaspPerCard { get => Relics["CloakClasp"]; set => SetRelic("CloakClasp", value); }
    [JsonIgnore] public int RippleBasinBlock { get => Relics["RippleBasin"]; set => SetRelic("RippleBasin", value); }
    [JsonIgnore] public int ScreamingFlagonDamage { get => Relics["ScreamingFlagon"]; set => SetRelic("ScreamingFlagon", value); }
    /// <summary>Stone Calendar's damage when this is its turn (Data: turns until then), else 0.</summary>
    [JsonIgnore] public int StoneCalendarDamage { get => Relics.DataOf("StoneCalendar") == 0 ? Relics["StoneCalendar"] : 0; set => SetRelic("StoneCalendar", value); }
    /// <summary>Obsidian Vice: damage per card in hand to a random enemy at end of turn (v0.113.0).</summary>
    [JsonIgnore] public int ObsidianViceDamage { get => Relics["ObsidianVice"]; set => SetRelic("ObsidianVice", value); }
    [JsonIgnore] public int CharonsAshesDamage { get => Relics["CharonsAshes"]; set => SetRelic("CharonsAshes", value); }
    [JsonIgnore] public int ForgottenSoulDamage { get => Relics["ForgottenSoul"]; set => SetRelic("ForgottenSoul", value); }
    /// <summary>Parrying Shield (amount: its damage; Data: the block it needs).</summary>
    [JsonIgnore] public int ParryingShieldDamage { get => Relics["ParryingShield"]; set => Relics = Relics.With("ParryingShield", value, Relics.DataOf("ParryingShield")); }
    [JsonIgnore] public int ParryingShieldBlock { get => Relics.DataOf("ParryingShield"); set => Relics = Relics.With("ParryingShield", Relics["ParryingShield"], value); }
    /// <summary>
    /// Pael's Eye is armed (unused this combat, no card played this turn): BeforeSideTurnEndEarly exhausts your whole hand.
    /// A card you play disarms it (BeforeCardPlayed); auto-plays don't.
    /// </summary>
    [JsonIgnore] public bool PaelsEyeExhaust
    {
        get => Relics.Has("PaelsEye") && (Relics.DataOf("PaelsEye") & 1) != 0;
        set
        {
            if (!value && !Relics.Has("PaelsEye")) return;
            Relics = Relics.With("PaelsEye", Relics["PaelsEye"], value ? Relics.DataOf("PaelsEye") | 1 : Relics.DataOf("PaelsEye") & ~1);
        }
    }

    // Hand and turn counters.
    public int HandCount;
    /// <summary>Arrows they can shoot now (PlayerCombatState.AvailableArrowCount; the Fool).</summary>
    public int Arrows;
    /// <summary>Energy now (PlayerCombatState.Energy): Pael's Tears.</summary>
    public int Energy;
    /// <summary>Ethereal cards in hand that exhaust at end of turn.</summary>
    public int Fading;
    public int AttacksThisTurn;
    /// <summary>End-of-turn damage from cards in hand (Burn, Decay...), blockable, in hand order.</summary>
    public List<(string Name, int Amount)> HandBlockable = new();
    /// <summary>End-of-turn HP loss from cards in hand (Bad Luck...).</summary>
    public List<(string Name, int Amount)> HandUnblockable = new();
    public int RegretCards;
    public List<(string Name, int Amount)> DebuffBlockable = new();
    /// <summary>Debuffs that land after Parrying Shield (AfterSideTurnEndLate: Disintegration).</summary>
    public List<(string Name, int Amount)> DebuffLateBlockable = new();
    public List<(string Name, int Amount)> DebuffUnblockable = new();

    // Orbs, front first.
    public List<SimOrb> Orbs = new();
    public int OrbCapacity;
    /// <summary>
    /// A character with no base orb slots: the first channel with no capacity adds a slot
    /// (OrbCmd.Channel → AddSlots 1). A Defect whose slots are all gone channels nothing.
    /// </summary>
    public bool SlotOnFirstChannel;
    /// <summary>Thunder: damage (unpowered) to each Lightning evoke's target that's still alive (ThunderPower.AfterOrbEvoked; 0 = none).</summary>
    public int Thunder;
    /// <summary>Extra passive triggers for whichever orb is in front when it fires (Gold-Plated Cables).</summary>
    public int FrontOrbBonusTriggers;

    // What happens to damage reaching your HP.
    /// <summary>Tungsten Rod (relic, amount = HpLossReduction).</summary>
    [JsonIgnore] public int TungstenRod { get => Relics["TungstenRod"]; set => SetRelic("TungstenRod", value); }

    /// <summary>Block gains so far (each sets off Juggernaut): counted for the Juggernaut tab.</summary>
    public int BlockGains;

    /// <summary>Envenom (EnvenomPower): poison on each enemy your attack gets damage through to (per hit).</summary>
    [JsonIgnore] public int Envenom { get => Powers["EnvenomPower"]; set => Set("EnvenomPower", value); }
    /// <summary>Snecko Skull (relic, its amount): extra poison on every application of yours.</summary>
    [JsonIgnore] public int PoisonBonus { get => Relics["SneckoSkull"]; set => SetRelic("SneckoSkull", value); }
    /// <summary>The Boot (relic, its amount): your attack hits that lose an enemy any HP lose it at least this much (0 = no relic).</summary>
    [JsonIgnore] public int BootMinimum { get => Relics["TheBoot"]; set => SetRelic("TheBoot", value); }
    /// <summary>Your Focus now (orb values already include it), for Focus changes during a play.</summary>
    public int Focus;
    /// <summary>Beating Remnant: HP you can still lose until the start of your next turn (it resets only then).</summary>
    /// <summary>Beating Remnant (relic: amount = MaxHpLoss, Data = HP lost since your turn started).</summary>
    [JsonIgnore] public int RemnantLeftPlayerTurn
    {
        get => Relics.Has("BeatingRemnant") ? Math.Max(0, Relics["BeatingRemnant"] - Relics.DataOf("BeatingRemnant")) : int.MaxValue;
        set { if (value != int.MaxValue) Relics = Relics.With("BeatingRemnant", Relics.Has("BeatingRemnant") ? Relics["BeatingRemnant"] : value, (Relics.Has("BeatingRemnant") ? Relics["BeatingRemnant"] : value) - value); }
    }
    [JsonIgnore] public int RemnantCap
    {
        get => Relics.Has("BeatingRemnant") ? Relics["BeatingRemnant"] : int.MaxValue;
        set
        {
            if (value == int.MaxValue) { Relics = Relics.Without("BeatingRemnant"); return; }
            var left = RemnantLeftPlayerTurn;
            Relics = Relics.With("BeatingRemnant", value, left == int.MaxValue ? 0 : Math.Max(0, value - left));
        }
    }
    [JsonIgnore] public int Buffer { get => Powers["BufferPower"]; set => Set("BufferPower", value); }
    public int OstyHp;
    /// <summary>Osty takes the hits aimed at his owner first (DieForYouPower).</summary>
    public bool OstyShields = true;
    /// <summary>
    /// Your pet Wuppy's HP (Wuppy relic, v0.113.0): while it lives, every powered attack aimed at you hits it instead,
    /// whole, before your block (WuppysLoyaltyPower.ModifyPreBlockDamageTarget). 0: none.
    /// </summary>
    public int WuppyHp;

    /// <summary>
    /// HP (int.MaxValue: not tracked). A teammate's hits stop at 0. Yours: reaching 0 uses up what prevents
    /// death (Fairy in a Bottle, then Lizard Tail), each healing you from 0; with none left, you die (SimState.Died).
    /// </summary>
    public int Hp = int.MaxValue;
    /// <summary>
    /// Fairy in a Bottle potions you hold (FairyInABottle.ShouldDie, checked before ShouldDieLate) and the HP each heals
    /// you to (30% of max HP, at least 1); then Lizard Tail's (50%, once: LizardTail.ShouldDieLate), 0 when used or none.
    /// </summary>
    public int Fairies, FairyHeal, LizardTailHeal;

    // Teammates only (yours are on SimState: Taken, OstyTaken, DamageThrough).
    /// <summary>A teammate's HP lost, through their block, and lost by their Osty this turn.</summary>
    public int Taken, Through, OstyTaken;
    /// <summary>A teammate's damage landing on them (before block) and block they gained: their Incoming Damage.</summary>
    public int Incoming, BlockGained;
    /// <summary>
    /// Beacon of Hope: half of each block this player gains on the players' side goes to every other
    /// living player (BeaconOfHopePower.AfterBlockGained). BeaconGiving: held while it gives (the game's
    /// guard against two Beacons trading block forever).
    /// </summary>
    public bool BeaconOfHope, BeaconGiving;

    // The auto-play phase (PlayerTurnPhase.AutoPostPlay: Hook.AfterAutoPostPlayPhaseEntered, before BeforeSideTurnEnd). Yours only.
    /// <summary>Stampede: auto-plays this many random Attacks from your hand (StampedePower; 0 = none).</summary>
    public int Stampede;
    /// <summary>Simpleton: auto-plays this many of your cheapest cards (a random one among ties; SimpletonPower; 0 = none).</summary>
    public int Simpleton;
    /// <summary>Your playable cards in hand, for Stampede and Simpleton to pick from (null when you have neither).</summary>
    public List<SimHandCard>? AutoHand;
    /// <summary>Howl from Beyond copies in your exhaust pile: each auto-plays (its play, one per copy).</summary>
    public List<SimPlay> ExhaustAutoPlays = new();
    /// <summary>
    /// I Am Invincible: its block when it's certainly the top of your draw pile (every card there is a copy of it: the
    /// order stays hidden otherwise); 0 = not certain. It auto-plays from the top (AutoPlayFromDrawPile).
    /// </summary>
    public int InvincibleBlock;
    /// <summary>Cards in your draw pile then (all I Am Invincible): a play that draws them all leaves it uncertain.</summary>
    public int InvincibleDrawPile;

    // Branches share their orbs until one changes them (OwnOrbs): copying them for every candidate
    // branch, most of which merge away as duplicates, was most of a branch-heavy simulation's cost.
    private bool _orbsShared;

    /// <summary>The orbs, to change (evoke, channel, Glass and Dark passives, Focus): copied for this branch first if shared.</summary>
    public List<SimOrb> OwnOrbs()
    {
        if (_orbsShared)
        {
            var own = new List<SimOrb>(Orbs.Count);
            foreach (var o in Orbs) own.Add(o.Clone());
            Orbs = own;
            _orbsShared = false;
        }
        return Orbs;
    }

    public SimPlayer Clone()
    {
        // The hand and debuff lists never change, so they're shared; the orbs until one branch changes them.
        var c = (SimPlayer)MemberwiseClone();
        // Both now point at one list: whichever changes it first copies it (an empty one too: a channel adds to it).
        _orbsShared = true;
        c._orbsShared = true;
        return c;
    }
}

/// <summary>A card in your hand that Stampede or Simpleton could auto-play.</summary>
public sealed class SimHandCard
{
    /// <summary>Which card (SimPlay.HandCard of its own play, so playing it takes it out of the hand).</summary>
    public int Id;
    public string Name = "";
    public bool IsAttack;
    /// <summary>Its cost now (X costs: 999999999, as SimpletonPower orders them).</summary>
    public int Cost;
    /// <summary>
    /// Its play when auto-played: one per hittable enemy for a card aimed at one (CardCmd.AutoPlay picks a random
    /// target), else one. Empty: its play can't be simulated (it changes the hand: draws, generates); nothing is
    /// predicted past it.
    /// </summary>
    public List<SimPlay> Plays = new();
}

/// <summary>One branch of the simulation: the state plus what happened so far.</summary>
public sealed class SimState
{
    public SimPlayer P = new();
    public List<SimEnemy> Enemies = new();

    /// <summary>Keep the display logs (Incoming Damage's own run). Off for card simulations: faster.</summary>
    public bool Log;

    /// <summary>Random hits land on nobody: the safe fallback when there are too many branches.</summary>
    public bool NoRandomHits;

    /// <summary>
    /// Branches ran out: random hits and poison land on the first possible target in place, without
    /// branching (this run only keeps "can happen" results; damage to you comes from the safe run).
    /// </summary>
    public bool FirstTargetOnly;

    /// <summary>A step ran out of branches inside itself (SimEngine.Then): the run is out of branches.</summary>
    internal bool OutOfBranches;

    // ---- results (per branch)
    public int Taken;
    /// <summary>You reach 0 HP with nothing left to prevent death (your HP tracked: SimPlayer.Hp).</summary>
    public bool Died;
    /// <summary>An auto-play at the end of your turn couldn't be simulated in this branch (Stampede or Simpleton picked a card that changes your hand): later auto-plays aren't either.</summary>
    public bool AutoPlaysUnknown;
    /// <summary>Cards auto-played at the end of your turn in this branch (Stampede, Simpleton, I Am Invincible, Howl from Beyond).</summary>
    public int AutoPlayed;
    /// <summary>The biggest powered hit (blocked plus HP lost) of the play so far (Mighty Wings).</summary>
    public int PlayBiggestHit;
    /// <summary>
    /// HP loss of yours during the play so far: times some got past your block to the HP-loss modifiers (Tungsten Rod
    /// flashes when it takes 1 off), times you lost HP (UnblockedDamage > 0: Self-Forming Clay, Emotion Chip, Centennial
    /// Puzzle, Demon Tongue, Inferno, Rupture), and the HP lost (Red Skull). Not in the branch key: merged as the smallest.
    /// </summary>
    public int PlayHpReached, PlayHpLosses, PlayHpLost;
    /// <summary>
    /// What fired during the play (the play log, docs/SIMULATOR_V2.md): source name → times (Amount) and the damage or block it
    /// caused (Data). Fired is the smallest over branches merged into this one (it happens in every one of them); FiredAny
    /// the largest (in some). Not in the branch key.
    /// </summary>
    public PowerSet Fired = PowerSet.Empty, FiredAny = PowerSet.Empty;
    /// <summary>What kept you alive this turn: Fairy in a Bottle potions used, and Lizard Tail.</summary>
    public int FairiesUsed;
    public bool LizardTailUsed;

    /// <summary>Damage your pet Wuppy took for you (enemy attacks it redirected).</summary>
    public int WuppyTaken;
    public int OstyTaken;
    public int DamageThrough;
    public int SkippedPoisoned;
    public int SkippedEndOfTurn;
    public int CancelledHits;
    /// <summary>Block you have when the enemy turn starts.</summary>
    public int BlockAtEnemyTurn;
    /// <summary>Your Parrying Shield fired at the end of your turn (your block at least its threshold).</summary>
    public bool ParryingShieldFired;
    public List<(string Name, int Amount)> EndOfTurnBlock = new();
    public List<(string Name, int Amount)> Blockable = new();
    public List<(string Name, int Amount)> Unblockable = new();
    public List<(string Name, int Amount)> EndOfTurnDamage = new();

    /// <summary>Running "blockable damage landed so far" for Imbalanced's full-block threshold.</summary>
    public int BlockDemand;
    public Dictionary<int, int> FullBlockNeeded = new();

    public SimEnemy? Enemy(int id) => Enemies.FirstOrDefault(e => e.Id == id);

    [JsonIgnore]
    public IEnumerable<SimEnemy> Targetable => Enemies.Where(e => e.Hittable && e.Alive);

    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true, WriteIndented = false };

    /// <summary>
    /// The state as JSON, for reports: a report's start state can be dropped into
    /// tests/SpireQoL.Sim.Tests/Cases to replay it offline (see docs/DEVELOPMENT.md).
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SimState FromJson(string json) => JsonSerializer.Deserialize<SimState>(json, JsonOptions)!;

    /// <summary>
    /// Fingerprint of everything in the state (every field, via its JSON), for caching simulations
    /// between frames. Unlike Key (branch merging), it changes when anything the simulation reads
    /// changes: intents, powers, relic values, hand cards.
    /// </summary>
    public long FullKey()
    {
        var json = ToJson();
        var h = unchecked((long)14695981039346656037UL);
        foreach (var ch in json)
        {
            unchecked
            {
                h ^= ch;
                h *= 1099511628211L;
            }
        }
        return h;
    }

    /// <summary>
    /// This branch's share of the rolls (1 at the start): each random target is equally likely, so a
    /// split divides it evenly, and identical branches merged together add theirs. For chances.
    /// </summary>
    public double Weight = 1;

    /// <summary>
    /// Other players in multiplayer, from what's on screen (orbs, powers, relics): only their effects on
    /// enemies at end of turn count; their block protects them, and their hand isn't known.
    /// </summary>
    public List<SimPlayer> Teammates = new();

    /// <summary>How many teammates come before you in player order (their orbs fire before yours).</summary>
    public int TeammatesBeforeMe;

    // Branches share their teammates until one changes them (Glass and Dark passives, Consuming
    // Shadow): copying them on every branch made multiplayer fights slow.
    private bool _teammatesShared;

    /// <summary>
    /// After a lead-in (plays still resolving or queued, decision #27): what this branch looked like when the
    /// card itself starts, so its own kills, block breaks and block gains are measured from there.
    /// </summary>
    internal PlayBaseline? PlayBase;

    /// <summary>
    /// The values of relics and powers (their DynamicVars), read from the game: "owner:Name:Var" → value. Shared by every branch
    /// and never changed; models read them with SimEngine.Var.
    /// </summary>
    public Dictionary<string, int> Vars = new();

    /// <summary>A card event is being dispatched (draw, exhaust, discard...): what fires then is an indirect proc (the Indirect procs setting).</summary>
    internal bool IndirectNow;

    /// <summary>The card play being resolved (its hooks and actions read it), and the attack in progress (Vigor, Aim...).</summary>
    internal SimCardPlay? CurrentPlay;
    internal int AttackSerial, CurrentAttack;
    /// <summary>The enemy turn is under way (the players' side's turn is over: Inferno doesn't react).</summary>
    internal bool EnemyTurnNow;
    /// <summary>Your side's turn is ending (EndOfTurn): block and damage from models go into the Incoming Damage breakdown.</summary>
    internal bool EndOfTurnNow;
    /// <summary>Relics' and powers' names as the game shows them ("id:Name"), for the Incoming Damage breakdown. Shared, never changed.</summary>
    public Dictionary<string, string> Titles = new();
    /// <summary>
    /// What models bound to a card play at BeforeCardPlayed (the game's amountsForPlayedCards: Afterimage, Gravity, Strangle...),
    /// "Name@owner" → amount; taken at AfterCardPlayed. A power the play itself applies isn't bound, so it doesn't fire on it.
    /// </summary>
    internal PowerSet Bound = PowerSet.Empty;
    /// <summary>The first hit of a card's attack on each enemy (enemy id → damage), for the damage-formula audit.</summary>
    internal PowerSet FirstHits = PowerSet.Empty;
    /// <summary>Card events raised inside a hit or a death, run once it's over (SimEngine.Raise, DrainPending).</summary>
    internal PowerSet Pending = PowerSet.Empty;
    /// <summary>The enemy a shot's arrow is flying at (spent before the attack, impaling after it); -1 none.</summary>
    internal int ArrowAwayAt = -1;
    /// <summary>The card a card event is about, when known (the played card exhausting itself: Burning Sticks).</summary>
    internal SimCard? EventCard;
    /// <summary>Card events during the current card play, by CardEvent name (Unceasing Top: nothing drawn or created).</summary>
    internal PowerSet PlayEvents = PowerSet.Empty;
    /// <summary>Block gains from the current card play (Unmovable doesn't count them).</summary>
    internal int PlayCardBlockGains;

    /// <summary>The listeners (SimEngine.Listeners) and the power and relic sets they were built from: rebuilt when one changes.</summary>
    internal List<(SimModel Model, int Owner)>? ListenerCache;
    internal PowerSet[]? ListenerSets;

    /// <summary>The run's work counter (SimEngine.Run sets it; every copy of the state shares it).</summary>
    internal SimWork? Work;

    internal sealed record PlayBaseline(HashSet<int> Alive, HashSet<int> Broken, int BlockGains);

    /// <summary>Teammate k, to change: copied for this branch first if it's shared.</summary>
    public SimPlayer MutableTeammate(int k)
    {
        if (_teammatesShared)
        {
            Teammates = Teammates.Select(t => t.Clone()).ToList();
            _teammatesShared = false;
        }
        return Teammates[k];
    }

    /// <summary>
    /// A copy without the cards in teammates' hands (decision #25: what's shown uses only what you can see).
    /// Their hand size stays (it's on screen: Cloak Clasp, Screaming Flagon, Regret's amount); the cards
    /// that act from their hand at end of turn (Burn, Decay, Regret) and their Ethereal cards (which exhaust: Feel No
    /// Pain, Charon's Ashes, Forgotten Soul) are left out.
    /// </summary>
    public SimState WithoutTeammateHands()
    {
        var shown = Clone();
        for (var k = 0; k < shown.Teammates.Count; k++)
        {
            var t = shown.MutableTeammate(k);
            t.HandBlockable = new();
            t.HandUnblockable = new();
            t.RegretCards = 0;
            t.Fading = 0;
            t.Stampede = t.Simpleton = 0;
            t.AutoHand = null;
            t.ExhaustAutoPlays = new();
            t.InvincibleBlock = t.InvincibleDrawPile = 0;
        }
        return shown;
    }

    /// <summary>An identical branch merged into this one: per-play results keep the guarantee (smallest) and the possibility (largest).</summary>
    public void MergeResults(SimState r)
    {
        PlayHpReached = Math.Min(PlayHpReached, r.PlayHpReached);
        PlayHpLosses = Math.Min(PlayHpLosses, r.PlayHpLosses);
        PlayHpLost = Math.Min(PlayHpLost, r.PlayHpLost);
        PlayBiggestHit = Math.Min(PlayBiggestHit, r.PlayBiggestHit);
        Fired = MergeLog(Fired, r.Fired, every: true);
        FiredAny = MergeLog(FiredAny, r.FiredAny, every: false);
    }

    /// <summary>Two play logs as one: every (the smaller of each, gone when either lacks it) or any (the larger).</summary>
    internal static PowerSet MergeLog(PowerSet a, PowerSet b, bool every)
    {
        if (ReferenceEquals(a, b)) return a;
        var r = every ? PowerSet.Empty : a;
        if (every)
        {
            foreach (var e in a.Items)
                if (b.Has(e.Name)) r = r.With(e.Name, Math.Min(e.Amount, b[e.Name]), Math.Min(e.Data, b.DataOf(e.Name)));
            return r;
        }
        foreach (var e in b.Items) r = r.With(e.Name, Math.Max(e.Amount, a[e.Name]), Math.Max(e.Data, a.DataOf(e.Name)));
        return r;
    }

    /// <summary>Logs that <paramref name="source"/> fired, with the damage or block it caused.</summary>
    /// <param name="owner">Whose source it is: an enemy's is also logged as "Name@id", so two enemies' powers stay apart.</param>
    public void LogFired(string source, int amount = 0, int? owner = null)
    {
        Log1(source, amount);
        if (owner is >= 0) Log1($"{source}@{owner}", amount);
        if (IndirectNow) Log1("~" + source, amount);
        if (IndirectNow && owner is >= 0) Log1($"~{source}@{owner}", amount);
    }

    /// <summary>An indirect firing (from a card event) is also logged as "~Name" (and "~Name@id" for an enemy's).</summary>
    private void Log1(string key, int amount)
    {
        Fired = Fired.With(key, Fired[key] + 1, Fired.DataOf(key) + amount);
        FiredAny = FiredAny.With(key, FiredAny[key] + 1, FiredAny.DataOf(key) + amount);
    }

    public SimState Clone()
    {
        if (Work != null) Work.Copies++;
        var c = (SimState)MemberwiseClone();
        c.P = P.Clone();
        _teammatesShared = true;
        c._teammatesShared = true;
        c.Enemies = new List<SimEnemy>(Enemies.Count);
        foreach (var e in Enemies) c.Enemies.Add(e.Clone());
        if (Log)
        {
            c.EndOfTurnBlock = new(EndOfTurnBlock);
            c.Blockable = new(Blockable);
            c.Unblockable = new(Unblockable);
            c.EndOfTurnDamage = new(EndOfTurnDamage);
        }
        return c;
    }

    /// <summary>
    /// Identifies branches that can't diverge any more (same numbers everywhere that matters), so
    /// they're merged after each random event. Results that only differ in their logs merge too;
    /// the first one's logs are kept.
    /// </summary>
    public long Key()
    {
        // FNV-1a over every number that can differ between branches of one run (64-bit: collisions
        // among a few hundred branches are vanishingly unlikely).
        var h = unchecked((long)14695981039346656037UL);
        void Add(long v)
        {
            unchecked
            {
                h ^= v;
                h *= 1099511628211L;
            }
        }

        Add(P.Block); Add(P.Arrows); Add(Taken); Add(OstyTaken); Add(P.Buffer); Add(P.OstyHp); Add(P.WuppyHp); Add(P.Hp); Add(P.Fairies); Add(P.LizardTailHeal); Add(Died ? 1 : 0); Add(P.RemnantLeftPlayerTurn); Add(ParryingShieldFired ? 1 : 0);
        Add(BlockDemand); Add(SkippedPoisoned); Add(SkippedEndOfTurn); Add(CancelledHits);
        foreach (var e in Enemies)
        {
            Add(e.Id); Add(e.Hp); Add(e.Block); Add(e.ShellLeft); Add(e.UnblockedInstances); Add(e.FirstHitFullyBlocked ? 1 : 0); Add(e.Poison); Add(e.DiedBeforeActing ? 1 : 0);
            Add(e.PoisonStacks); Add(e.CurlUp); Add(e.Skittish);
            Add(e.Stunned ? 1 : 0); Add(e.Burrowed ? 1 : 0); Add(e.Asleep ? 1 : 0); Add(e.Slumber); Add(e.Flutter); Add(e.StunAtHp); Add(e.Gambit ? 1 : 0); Add(e.Slippery);
            Add(e.ReactedWithBlock ? 1 : 0); Add(e.BlockBroken ? 1 : 0);
            e.Powers.Hash(Add);
            Add(e.HitsAfterAllyDies == null ? 0 : 1); Add(e.Hits.Count); foreach (var hit in e.Hits) Add(hit); Add(e.GambitTriggered ? 1 : 0); Add(e.SlipperyTriggered ? 1 : 0);
        }
        if (P.AutoHand != null)
        {
            Add(P.AutoHand.Count);
            foreach (var c in P.AutoHand) Add(c.Id);
        }
        Add(AutoPlaysUnknown ? 1 : 0); Add(AutoPlayed);
        foreach (var e in Enemies) Add(e.Exploded ? 1 : 0);
        P.Powers.Hash(Add); P.Relics.Hash(Add);
        Add(P.Orbs.Count);
        foreach (var o in P.Orbs)
        {
            Add((int)o.Kind); Add(o.Passive); Add(o.Evoke);
        }
        return h;
    }
}

/// <summary>
/// What one simulation costs, the same on every machine (the performance tests limit these, not time).
/// One run is one thread: plain fields, never shared between runs.
/// </summary>
internal sealed class SimWork
{
    /// <summary>Steps applied to the branch set (each hit, orb, block gain, the enemy turn).</summary>
    public int Steps;
    /// <summary>Branches stepped, summed over steps.</summary>
    public int Stepped;
    /// <summary>State copies (most of the cost: every random outcome is a copy).</summary>
    public int Copies;
    /// <summary>The most copies one step made.</summary>
    public int MaxStepCopies;
    private int _stepStart;

    /// <summary>A new step starts (BranchSet).</summary>
    public void StartStep()
    {
        EndStep();
        _stepStart = Copies;
    }

    public void EndStep() => MaxStepCopies = Math.Max(MaxStepCopies, Copies - _stepStart);

    /// <summary>This step has made SimEngine.StepCopyBudget copies: random choices stop branching.</summary>
    public bool OverBudget => Copies - _stepStart >= SimEngine.StepCopyBudget;
}

/// <summary>One hit of an enemy's attack intent: its base damage (AttackIntent.DamageCalc) and, per player id, the captured terms.</summary>
public sealed record IntentHit(decimal Base)
{
    public IReadOnlyDictionary<int, CapturedTerms>? Captured { get; init; }
    public CapturedTerms TermsOn(int player) => Captured != null && Captured.TryGetValue(player, out var c) ? c : CapturedTerms.None;
}

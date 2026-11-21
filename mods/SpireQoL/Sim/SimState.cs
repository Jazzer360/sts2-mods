using System.Collections.Immutable;
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

    /// <summary>For branch keys (SimState.Key): every number of the orb.</summary>
    internal void Hash(Action<long> add)
    {
        add((int)Kind); add(Passive); add(Evoke); add(Triggers); add(GlassBase); add(GlassBonus);
    }
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
        get => Powers.Has(PowerId.HardenedShell) ? Powers.DataOf(PowerId.HardenedShell) : int.MaxValue;
        set
        {
            if (!Powers.Has(PowerId.HardenedShell) && value == int.MaxValue) return; // no shell: nothing to keep
            Powers = Powers.With(PowerId.HardenedShell, Powers.Has(PowerId.HardenedShell) ? Powers[PowerId.HardenedShell] : int.MaxValue, value);
        }
    }
    [JsonIgnore] public int ShellPerTurn
    {
        get => Powers.Has(PowerId.HardenedShell) ? Powers[PowerId.HardenedShell] : int.MaxValue;
        set => Powers = value == int.MaxValue ? Powers.Without(PowerId.HardenedShell)
            : Powers.With(PowerId.HardenedShell, value, Powers.Has(PowerId.HardenedShell) ? Powers.DataOf(PowerId.HardenedShell) : value);
    }
    /// <summary>Damage of its poison tick at the start of its turn.</summary>
    public int Poison;
    /// <summary>Keeps its block between turns (BarricadePower, or BurrowedPower.ShouldClearBlock).</summary>
    [JsonIgnore] public bool Barricade { get => Powers.Has(PowerId.Barricade) || Powers.Has(PowerId.Burrowed); set => Set(PowerId.Barricade, value ? 1 : 0); }
    public bool Stunned;
    /// <summary>Its attack this turn: damage of each hit on you (the intent's numbers).</summary>
    public List<int> Hits = new();
    /// <summary>
    /// Its attack as pipeline inputs (docs/SIMULATOR.md): each hit's base and the captured terms of unmodelled sources on each
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
        get => Powers.Has(PowerId.Intangible) ? 1 : Powers.Has(PowerId.HardToKill) ? Powers[PowerId.HardToKill] : int.MaxValue;
        set => Set(PowerId.HardToKill, value == int.MaxValue ? 0 : value);
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
    [JsonIgnore] public int CurlUp { get => Powers[PowerId.CurlUp]; set => Set(PowerId.CurlUp, value); }
    /// <summary>Sneaky: block it gains after each Attack you play.</summary>
    [JsonIgnore] public int Sneaky { get => Powers[PowerId.Sneaky]; set => Set(PowerId.Sneaky, value); }
    /// <summary>Skittish: block it gains after a card's attack gets damage through its block (once per turn; 0 once used).</summary>
    [JsonIgnore] public int Skittish { get => Powers[PowerId.Skittish]; set => Set(PowerId.Skittish, value); }
    /// <summary>It gained block from Curl Up, Sneaky or Skittish because of the play.</summary>
    public bool ReactedWithBlock;

    // Being hit (decompile v0.111.0):
    /// <summary>
    /// Thorns: damage to you (blockable) before each hit of your card's attack on it
    /// (ThornsPower.BeforeDamageReceived on powered attacks), after the game's damage math (0 = none).
    /// </summary>
    [JsonIgnore] public int Thorns { get => Powers[PowerId.Thorns]; set => Set(PowerId.Thorns, value); }
    /// <summary>Burrowed: keeps its block between turns; breaking that block stuns it (AfterBlockBroken), and the power and its block go.</summary>
    [JsonIgnore] public bool Burrowed { get => Powers.Has(PowerId.Burrowed); set => Set(PowerId.Burrowed, value ? 1 : 0); }
    /// <summary>Asleep (Lagavulin Matriarch): any damage through its block wakes and stuns it (AsleepPower.AfterDamageReceived).</summary>
    [JsonIgnore] public bool Asleep { get => Powers.Has(PowerId.Asleep); set => Set(PowerId.Asleep, value ? 1 : 0); }
    /// <summary>
    /// Slumber (Slumbering Beetle): each damage instance through its block takes a stack; the last one wakes it, stunned
    /// (SlumberPower.AfterDamageReceived). 0: none.
    /// </summary>
    [JsonIgnore] public int Slumber { get => Powers[PowerId.Slumber]; set => Set(PowerId.Slumber, value); }
    /// <summary>Flutter: powered-attack hits with damage through its block left before it's stunned (0 = none).</summary>
    [JsonIgnore] public int Flutter { get => Powers[PowerId.Flutter]; set => Set(PowerId.Flutter, value); }
    /// <summary>Plow / Shriek: stunned when damage gets through and leaves it at or below this HP (0 = none).</summary>
    [JsonIgnore] public int StunAtHp { get => Math.Max(Powers[PowerId.Plow], Powers[PowerId.Shriek]); set => Set(PowerId.Plow, value); }
    /// <summary>The Gambit: dies when a powered attack gets damage through.</summary>
    [JsonIgnore] public bool Gambit { get => Powers.Has(PowerId.TheGambit); set => Set(PowerId.TheGambit, value ? 1 : 0); }
    public bool GambitTriggered;
    /// <summary>Slippery: hits that lose it at most 1 HP each, left (0 = none).</summary>
    [JsonIgnore] public int Slippery { get => Powers[PowerId.Slippery]; set => Set(PowerId.Slippery, value); }
    public bool SlipperyTriggered;
    /// <summary>Alive when the simulation starts (deaths after that set off Crab Rage and Ravenous).</summary>
    public bool StartedAlive = true;
    /// <summary>Ravenous: an ally dying stuns it (RavenousPower.AfterDeath).</summary>
    [JsonIgnore] public bool Ravenous { get => Powers.Has(PowerId.Ravenous); set => Set(PowerId.Ravenous, value ? 1 : 0); }
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

    /// <summary>Its powers the simulator reads or changes (docs/SIMULATOR.md), in the order they were applied.</summary>
    public PowerSet Powers = PowerSet.Empty;

    /// <summary>Sets a power's amount (0 removes it).</summary>
    private void Set(PowerId power, int amount) => Powers = amount == 0 ? Powers.Without(power) : Powers.With(power, amount);

    /// <summary>Its block was broken (taken from above 0 to 0 by damage) during your turn.</summary>
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
        int Tick(int stackCount)
        {
            var sum = 0;
            for (var i = 0; i < Math.Min(stackCount, Math.Max(1, PoisonTriggers)); i++) sum += stackCount - i;
            return sum;
        }
        Poison += Tick(PoisonStacks + stacks) - Tick(PoisonStacks);
        PoisonStacks += stacks;
    }

    /// <summary>Its HP when your end of turn is over (before its turn), for the outcome audit.</summary>
    public int HpAtTurnEnd;

    /// <summary>
    /// Its hits through block when your end of turn is over (before its turn), for the outcome audit, which counts the hits of your
    /// end of turn; UnblockedInstances goes on through its turn (Necro Mastery when it hits Osty), which wake rules want.
    /// </summary>
    public int UnblockedAtTurnEnd;

    [JsonIgnore]
    public bool Alive => Hp > 0;

    /// <summary>Copies the numbers; the attack list never changes during a run, so it's shared.</summary>
    public SimEnemy Clone() => (SimEnemy)MemberwiseClone();

    /// <summary>For branch keys (SimState.Key): every number that can differ between branches of one run.</summary>
    internal void Hash(Action<long> add)
    {
        add(Id); add(Hp); add(Block); add(UnblockedInstances); add(FirstHitFullyBlocked ? 1 : 0); add(Poison); add(DiedBeforeActing ? 1 : 0);
        add(PoisonStacks); add(Stunned ? 1 : 0); add(ReactedWithBlock ? 1 : 0); add(BlockBroken ? 1 : 0); add(GambitTriggered ? 1 : 0);
        add(SlipperyTriggered ? 1 : 0); add(Exploded ? 1 : 0);
        Powers.Hash(add); // Hardened Shell, Curl Up, Skittish, Burrowed, Asleep, Slumber, Flutter, Plow, The Gambit, Slippery...
        add(Hits.Count); foreach (var hit in Hits) add(hit);
        add(HitsAfterAllyDies?.Count ?? -1); if (HitsAfterAllyDies != null) foreach (var hit in HitsAfterAllyDies) add(hit);
    }
}

/// <summary>A card in hand that acts when its owner's turn ends: its class name, its title, and its Damage or HpLoss value.</summary>
public sealed record TurnEndCard(string Card, string Title, int Amount);

public sealed class SimPlayer
{
    public int Block;

    private void Set(PowerId power, int amount) => Powers = amount == 0 ? Powers.Without(power) : Powers.With(power, amount);
    private void SetRelic(RelicId relic, int amount) => Relics = amount == 0 ? Relics.Without(relic) : Relics.With(relic, amount);

    /// <summary>Their powers the simulator reads or changes, in the order they were applied (docs/SIMULATOR.md).</summary>
    public PowerSet Powers = PowerSet.Empty;
    /// <summary>Their relics the simulator models, in relic order, with each one's counter (Amount) and flag (Data).</summary>
    public RelicSet Relics = RelicSet.Empty;
    /// <summary>
    /// What they played this turn, as the hooks count it (the game's CardPlaysStarted and CardPlaysFinished), under SimKeys.Plays: "Started",
    /// "Started:Attack", "Finished:Skill", "Finished#Shiv" (by tag)... Each play of a replay counts.
    /// </summary>
    public NamedCounts Turn = NamedCounts.Empty;

    // Powers that react or act at end of turn.
    [JsonIgnore] public int Juggernaut { get => Powers[PowerId.Juggernaut]; set => Set(PowerId.Juggernaut, value); }
    [JsonIgnore] public int Plating { get => Powers[PowerId.Plating]; set => Set(PowerId.Plating, value); }
    [JsonIgnore] public int FeelNoPain { get => Powers[PowerId.FeelNoPain]; set => Set(PowerId.FeelNoPain, value); }
    // End-of-turn powers and relics, as views onto their entries (the models read the entries: Sim/Models/EndOfTurnModels.cs).
    /// <summary>Hailstorm (amount; Data = the Frost orbs it needs).</summary>
    [JsonIgnore] public int Hailstorm { get => Powers[PowerId.Hailstorm]; set => Powers = value == 0 ? Powers.Without(PowerId.Hailstorm) : Powers.With(PowerId.Hailstorm, value, Powers.Has(PowerId.Hailstorm) ? Powers.DataOf(PowerId.Hailstorm) : 1); }
    [JsonIgnore] public int HailstormFrostNeeded { get => Powers.Has(PowerId.Hailstorm) ? Powers.DataOf(PowerId.Hailstorm) : 1; set => Powers = Powers.With(PowerId.Hailstorm, Powers[PowerId.Hailstorm], value); }
    /// <summary>The Bomb's damage when it goes off this turn end (its last tick: amount 1; Data = its damage), else 0.</summary>
    [JsonIgnore] public int TheBombDamage { get => Powers.Has(PowerId.TheBomb) && Powers[PowerId.TheBomb] <= 1 ? Powers.DataOf(PowerId.TheBomb) : 0; set => Powers = value == 0 ? Powers.Without(PowerId.TheBomb) : Powers.With(PowerId.TheBomb, amount: 1, value); }
    [JsonIgnore] public int ConsumingShadow { get => Powers[PowerId.ConsumingShadow]; set => Set(PowerId.ConsumingShadow, value); }
    [JsonIgnore] public int Thorns { get => Powers[PowerId.Thorns]; set => Set(PowerId.Thorns, value); }
    [JsonIgnore] public int FlameBarrier { get => Powers[PowerId.FlameBarrier]; set => Set(PowerId.FlameBarrier, value); }
    public bool Reflect;
    /// <summary>
    /// Inferno: each time you lose HP on your turn, this much damage to every enemy (InfernoPower.AfterDamageReceived:
    /// UnblockedDamage &gt; 0 while it's their side's turn; unpowered). Any player's (the model is the owner's).
    /// </summary>
    [JsonIgnore] public int Inferno { get => Powers[PowerId.Inferno]; set => Set(PowerId.Inferno, value); }

    // Relics (amount: their block or damage; Data where said).
    [JsonIgnore] public int OrichalcumBlock { get => Relics[RelicId.Orichalcum]; set => SetRelic(RelicId.Orichalcum, value); }
    [JsonIgnore] public int CloakClaspPerCard { get => Relics[RelicId.CloakClasp]; set => SetRelic(RelicId.CloakClasp, value); }
    [JsonIgnore] public int RippleBasinBlock { get => Relics[RelicId.RippleBasin]; set => SetRelic(RelicId.RippleBasin, value); }
    [JsonIgnore] public int ScreamingFlagonDamage { get => Relics[RelicId.ScreamingFlagon]; set => SetRelic(RelicId.ScreamingFlagon, value); }
    /// <summary>Stone Calendar's damage when this is its turn (Data: turns until then), else 0.</summary>
    [JsonIgnore] public int StoneCalendarDamage { get => Relics.DataOf(RelicId.StoneCalendar) == 0 ? Relics[RelicId.StoneCalendar] : 0; set => SetRelic(RelicId.StoneCalendar, value); }
    /// <summary>Obsidian Vice: damage per card in hand to a random enemy at end of turn (v0.113.0).</summary>
    [JsonIgnore] public int ObsidianViceDamage { get => Relics[RelicId.ObsidianVice]; set => SetRelic(RelicId.ObsidianVice, value); }
    [JsonIgnore] public int CharonsAshesDamage { get => Relics[RelicId.CharonsAshes]; set => SetRelic(RelicId.CharonsAshes, value); }
    [JsonIgnore] public int ForgottenSoulDamage { get => Relics[RelicId.ForgottenSoul]; set => SetRelic(RelicId.ForgottenSoul, value); }
    /// <summary>Parrying Shield (amount: its damage; Data: the block it needs).</summary>
    [JsonIgnore] public int ParryingShieldDamage { get => Relics[RelicId.ParryingShield]; set => Relics = Relics.With(RelicId.ParryingShield, value, Relics.DataOf(RelicId.ParryingShield)); }
    [JsonIgnore] public int ParryingShieldBlock { get => Relics.DataOf(RelicId.ParryingShield); set => Relics = Relics.With(RelicId.ParryingShield, Relics[RelicId.ParryingShield], value); }
    /// <summary>
    /// Pael's Eye is armed (unused this combat, no card played this turn): BeforeSideTurnEndEarly exhausts your whole hand.
    /// A card you play disarms it (BeforeCardPlayed); auto-plays don't.
    /// </summary>
    [JsonIgnore] public bool PaelsEyeExhaust
    {
        get => Relics.Has(RelicId.PaelsEye) && (Relics.DataOf(RelicId.PaelsEye) & 1) != 0;
        set
        {
            if (!value && !Relics.Has(RelicId.PaelsEye)) return;
            Relics = Relics.With(RelicId.PaelsEye, Relics[RelicId.PaelsEye], value ? Relics.DataOf(RelicId.PaelsEye) | 1 : Relics.DataOf(RelicId.PaelsEye) & ~1);
        }
    }

    // Hand and turn counters.
    public int HandCount;
    /// <summary>
    /// Their cards being played from their hand: each has left it (the game moves it before it plays), but HandCount counts it until
    /// it resolves (CardResolved), so a draw meanwhile has its slot (SimEngine.DrawOne). More than one when a play plays another
    /// (Hellraiser's Strike, drawn by a card being played).
    /// </summary>
    public int PlayingFromHand;
    /// <summary>Arrows they can shoot now (PlayerCombatState.AvailableArrowCount; the Fool).</summary>
    public int Arrows;
    /// <summary>Energy now (PlayerCombatState.Energy): Pael's Tears.</summary>
    public int Energy;
    /// <summary>Ethereal cards in hand that exhaust at end of turn.</summary>
    public int Fading;
    /// <summary>
    /// Your draw pile and discard pile by profile (decision #90): draws come from it, and what's drawn joins the hand. Null: draws
    /// are only counted (a teammate's; tests that don't set it).
    /// </summary>
    public SimDrawPile? Draw;
    /// <summary>Cards drawn in this simulation (each drawn card's id in AutoHand).</summary>
    public int Drawn;
    /// <summary>Status cards drawn this turn, the simulation's included (Iteration draws on the first).</summary>
    public int StatusDrawn;
    /// <summary>
    /// Of those, the ones Ethereal only through Hex (HexPower.TryModifyKeywordsInCombat on its Hexed cards), and the enemy that
    /// cast it (Who.None: none). Hex is removed when its caster dies (HexPower.AfterDeath), and those cards stop being Ethereal: they
    /// stay in hand at end of turn (decision #85).
    /// </summary>
    public int HexFading;
    [JsonConverter(typeof(CreatureIdJson))]
    public int HexFrom = Who.None;
    public int AttacksThisTurn;
    /// <summary>
    /// Cards in hand that act when the turn ends (CardModel.OnTurnEndInHand), in hand order: what each does is the simulator's
    /// (SimEngine.HandAtTurnEnd). Never changed in place (shared between clones).
    /// </summary>
    public List<TurnEndCard> TurnEndCards = new();
    /// <summary>
    /// Cards in their hand that act when they're exhausted (their own AfterCardExhausted: Drum of Battle's energy), each with its
    /// id in the hand (SimPlay.HandCard; drawn: -1000 - Drawn). A play exhausting from the hand sets them off (ExhaustFromHandAction);
    /// one played leaves the list as it leaves the hand. Never changed in place (shared between clones).
    /// </summary>
    public List<ExhaustingCard> ExhaustingInHand = new();

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
    [JsonIgnore] public int TungstenRod { get => Relics[RelicId.TungstenRod]; set => SetRelic(RelicId.TungstenRod, value); }

    /// <summary>Block gains so far (each sets off Juggernaut): counted for the Juggernaut tab.</summary>
    public int BlockGains;

    /// <summary>Envenom (EnvenomPower): poison on each enemy your attack gets damage through to (per hit).</summary>
    [JsonIgnore] public int Envenom { get => Powers[PowerId.Envenom]; set => Set(PowerId.Envenom, value); }
    /// <summary>Snecko Skull (relic, its amount): extra poison on every application of yours.</summary>
    [JsonIgnore] public int PoisonBonus { get => Relics[RelicId.SneckoSkull]; set => SetRelic(RelicId.SneckoSkull, value); }
    /// <summary>The Boot (relic, its amount): your attack hits that lose an enemy any HP lose it at least this much (0 = no relic).</summary>
    [JsonIgnore] public int BootMinimum { get => Relics[RelicId.TheBoot]; set => SetRelic(RelicId.TheBoot, value); }
    /// <summary>Your Focus now (orb values already include it), for Focus changes during a play.</summary>
    public int Focus;
    /// <summary>Beating Remnant (relic: amount = MaxHpLoss, Data = HP lost since your turn started).</summary>
    [JsonIgnore] public int RemnantLeftPlayerTurn
    {
        get => Relics.Has(RelicId.BeatingRemnant) ? Math.Max(0, Relics[RelicId.BeatingRemnant] - Relics.DataOf(RelicId.BeatingRemnant)) : int.MaxValue;
        set { if (value != int.MaxValue) Relics = Relics.With(RelicId.BeatingRemnant, Relics.Has(RelicId.BeatingRemnant) ? Relics[RelicId.BeatingRemnant] : value, (Relics.Has(RelicId.BeatingRemnant) ? Relics[RelicId.BeatingRemnant] : value) - value); }
    }
    [JsonIgnore] public int RemnantCap
    {
        get => Relics.Has(RelicId.BeatingRemnant) ? Relics[RelicId.BeatingRemnant] : int.MaxValue;
        set
        {
            if (value == int.MaxValue) { Relics = Relics.Without(RelicId.BeatingRemnant); return; }
            var left = RemnantLeftPlayerTurn;
            Relics = Relics.With(RelicId.BeatingRemnant, value, left == int.MaxValue ? 0 : Math.Max(0, value - left));
        }
    }
    [JsonIgnore] public int Buffer { get => Powers[PowerId.Buffer]; set => Set(PowerId.Buffer, value); }
    public int OstyHp;
    /// <summary>Osty takes the hits aimed at his owner first (DieForYouPower).</summary>
    public bool OstyShields = true;
    /// <summary>
    /// Your pet Wuppy's HP (Wuppy relic, v0.113.0): while it lives, every powered attack aimed at you hits it instead,
    /// whole, before your block (WuppysLoyaltyPower.ModifyPreBlockDamageTarget). 0: none.
    /// </summary>
    public int WuppyHp;

    /// <summary>
    /// HP (int.MaxValue: not tracked). Reaching 0 uses up what prevents death (Fairy in a Bottle, then Lizard Tail), each
    /// healing the player from 0; with none left, they die (Died). The same for every player (decision #93).
    /// </summary>
    public int Hp = int.MaxValue;
    /// <summary>
    /// Fairy in a Bottle potions you hold (FairyInABottle.ShouldDie, checked before ShouldDieLate) and the HP each heals
    /// you to (30% of max HP, at least 1); then Lizard Tail's (50%, once: LizardTail.ShouldDieLate), 0 when used or none.
    /// </summary>
    public int Fairies, FairyHeal, LizardTailHeal;

    // ---- results, for every player (decision #93: one engine; yours are read through SimState too: Taken, Died...)
    /// <summary>HP lost (after block and the HP-loss modifiers, overkill included), damage through block, and lost by Osty.</summary>
    public int Taken, Through, OstyTaken;
    /// <summary>The part of Taken no block could stop (Regret, Beckon, Bad Luck: SimEngine.LandUnblockable).</summary>
    public int UnblockableTaken;
    /// <summary>Blockable damage landing on the player (before block): their Incoming Damage. Block a teammate gained.</summary>
    public int Incoming, BlockGained;
    /// <summary>Damage Wuppy took for the player (hits it redirected: never reaches them).</summary>
    public int WuppyTaken;
    /// <summary>Reached 0 HP with nothing left to prevent it; what kept them alive (Fairy in a Bottle potions used, Lizard Tail).</summary>
    public bool Died, LizardTailUsed;
    public int FairiesUsed;
    /// <summary>
    /// Beacon of Hope: half of each block this player gains on the players' side goes to every other
    /// living player (BeaconOfHopePower.AfterBlockGained). BeaconGiving: held while it gives (the game's
    /// guard against two Beacons trading block forever).
    /// </summary>
    public bool BeaconOfHope, BeaconGiving;

    // The auto-play phase (PlayerTurnPhase.AutoPostPlay: Hook.AfterAutoPostPlayPhaseEntered, before BeforeSideTurnEnd), every
    // player's. A teammate's Stampede and Simpleton aren't filled (their hand; decision #80); their piles' auto-plays are.
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
            foreach (var orb in Orbs) own.Add(orb.Clone());
            Orbs = own;
            _orbsShared = false;
        }
        return Orbs;
    }

    /// <summary>
    /// For branch keys (SimState.Key): a player's numbers that can differ between branches of one run, the same for you and each
    /// teammate. Two branches that differ only in a player (their draws, block, HP, orbs, what fired for them) mustn't merge, or one
    /// of them takes the other's state.
    /// </summary>
    internal void Hash(Action<long> add)
    {
        add(Block); add(Hp); add(Taken); add(Through); add(OstyTaken); add(UnblockableTaken); add(Incoming); add(BlockGained);
        add(WuppyHp); add(WuppyTaken); add(OstyHp); add(Died ? 1 : 0); add(LizardTailUsed ? 1 : 0); add(LizardTailHeal); add(FairiesUsed); add(Fairies);
        add(BlockGains); add(Energy); add(HandCount); add(PlayingFromHand); add(Arrows); add(Fading); add(HexFading); add(HexFrom); add(Drawn); add(StatusDrawn);
        add(AttacksThisTurn); add(Focus); add(Thunder); add(OrbCapacity); add(BeaconGiving ? 1 : 0); add(InvincibleBlock); add(InvincibleDrawPile);
        Draw?.Hash(add);
        add(TurnEndCards.Count);
        foreach (var turnEndCard in TurnEndCards) { add(turnEndCard.Card.GetHashCode()); add(turnEndCard.Amount); }
        // By what each one does, in any order (copies that act the same are the same hand, as for AutoHand).
        add(ExhaustingInHand.Count);
        foreach (var exhausting in ExhaustingInHand.OrderBy(card => card.Energy).ThenBy(card => card.Type)) { add(exhausting.Energy); add((int)exhausting.Type); }
        add(AutoHand?.Count ?? -1);
        // By what each card is, in any order: branches that picked identical copies hold the same hand, and merge.
        if (AutoHand != null) foreach (var key in AutoHand.Select(handCard => handCard.Key).OrderBy(key => key.Digest)) add(key.GetHashCode());
        Powers.Hash(add); Relics.Hash(add); Turn.Hash(add);
        add(Orbs.Count);
        foreach (var orb in Orbs) orb.Hash(add);
    }

    public SimPlayer Clone()
    {
        // The hand and debuff lists never change, so they're shared; the orbs until one branch changes them.
        var player = (SimPlayer)MemberwiseClone();
        // Both now point at one list: whichever changes it first copies it (an empty one too: a channel adds to it).
        _orbsShared = true;
        player._orbsShared = true;
        return player;
    }
}

/// <summary>
/// An auto-play from your hand in one branch: what the card is (SimHandCard.Key: copies the simulator plays the same are one pick,
/// so a branch that picked either copy is one branch) and the enemy it was aimed at (Who.None: none).
/// </summary>
public sealed record SimPick(HandCardKey Card, [property: JsonConverter(typeof(CreatureIdJson))] int Target);

/// <summary>
/// What a hand card is to the simulator (SimHandCard.Key): its name, and a digest of everything else it holds. Two copies with one
/// key play the same in every way the simulator follows (decision #140).
/// </summary>
public readonly record struct HandCardKey(string Name, long Digest)
{
    public override string ToString() => $"{Name}:{Digest:X16}";
}

/// <summary>
/// A card that acts when it's exhausted (SimPlayer.ExhaustingInHand): which card in the hand it is, the energy it gives (Drum of
/// Battle's Energy, from its own AfterCardExhausted), and its type (what a filtered exhaust looks at).
/// </summary>
public sealed record ExhaustingCard(int HandCard, int Energy, SimCardType Type);

/// <summary>A card in your hand that Stampede or Simpleton could auto-play.</summary>
public sealed class SimHandCard
{
    /// <summary>Which card (SimPlay.HandCard of its own play, so playing it takes it out of the hand).</summary>
    public int Id;
    /// <summary>
    /// What the card is to the simulator: its name and a digest of everything else it holds (its cost, whether it's an attack, every
    /// play and action), apart from which card it is (Id, and its plays' HandCard). Copies with one key play the same in every way the
    /// simulator follows, so picking either is one branch (decision #140), and the check matches the game's pick by it. Read once
    /// (a hand card isn't changed once built).
    /// </summary>
    [JsonIgnore]
    public HandCardKey Key => _key ??= ContentKey();
    private HandCardKey? _key;

    private HandCardKey ContentKey()
    {
        var content = JsonSerializer.SerializeToNode(this, SimJson.Options)!.AsObject();
        content.Remove(nameof(Id));
        foreach (var play in content[nameof(Plays)]!.AsArray())
            if (Id != 0 && play![nameof(SimPlay.HandCard)]!.GetValue<int>() == Id) play[nameof(SimPlay.HandCard)] = "this card"; // played from your hand as itself (not 0: none)
        var digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content.ToJsonString()));
        return new HandCardKey(Name, BitConverter.ToInt64(digest, startIndex: 0));
    }

    /// <summary>Another copy of this card in your hand (one drawn): its own id, the same plays, so the same key.</summary>
    /// <remarks>Its own plays become the copy's (SimPlay.HandCard): played, it's the copy that leaves your hand.</remarks>
    public SimHandCard CopyWithId(int id) => new()
    {
        Id = id, Name = Name, IsAttack = IsAttack, Cost = Cost, _key = Key,
        Plays = Id == 0 ? Plays : Plays.Select(play => play.HandCard == Id ? play.AsHandCard(id) : play).ToList(),
    };

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
    /// <summary>You: your own player (every other player is in <see cref="Teammates"/>; any player by id: <see cref="Player"/>).</summary>
    [JsonPropertyName("P")] // the name reports and replay cases hold it under
    public SimPlayer You = new();
    public List<SimEnemy> Enemies = new();

    /// <summary>Keep the display logs (Incoming Damage's own run). Off for card simulations: faster.</summary>
    public bool Log;

    // ---- results (per branch). Yours are on your SimPlayer like every player's (decision #93); these read them.
    [JsonIgnore] public int Taken { get => You.Taken; set => You.Taken = value; }
    /// <summary>The part of Taken that no block could stop (Regret, Beckon, Bad Luck: SimEngine.LandUnblockable), after the HP-loss modifiers.</summary>
    [JsonIgnore] public int UnblockableTaken { get => You.UnblockableTaken; set => You.UnblockableTaken = value; }
    /// <summary>You reach 0 HP with nothing left to prevent death (your HP tracked: SimPlayer.Hp).</summary>
    [JsonIgnore] public bool Died { get => You.Died; set => You.Died = value; }
    /// <summary>An auto-play at the end of your turn couldn't be simulated in this branch (Stampede or Simpleton picked a card that changes your hand): later auto-plays aren't either.</summary>
    public bool AutoPlaysUnknown;
    /// <summary>
    /// Something in this branch the simulator doesn't model yet (decision #92, rule 2): why, or null. Such a branch counts as unknown
    /// in every chance and guarantee (a range and a ?), and the reason is counted by the audits as a gap to model.
    /// </summary>
    public string? Unmodelled;
    /// <summary>
    /// The cards a draw gave, while a card that acts on them draws (SimEngine.RunDrawnCard, decision #118); null otherwise. Set
    /// and cleared inside one step; in the key while set, as a set (decision #120): branches that drew the same cards in another
    /// order merge between its draws, ones that drew different cards don't.
    /// </summary>
    internal List<(SimDrawCard Card, int Id)>? DrawRecord;
    /// <summary>
    /// What the play's draw gave, for its chance tab (decision #119): Escape Plan 1 when it drew a Skill (else 0), Pillage the Attacks
    /// it drew, Scrape the cards it drew that cost 0. -1: no such draw. Part of the key, so the outcomes keep it apart.
    /// </summary>
    public int DrawnTally = -1;
    /// <summary>Cards auto-played at the end of your turn in this branch (Stampede, Simpleton, I Am Invincible, Howl from Beyond).</summary>
    public int AutoPlayed;
    /// <summary>
    /// Your auto-plays from your hand in this branch (Stampede, Simpleton), in order: the card and the enemy it was aimed at (-1:
    /// none). The forecast check keeps the branches whose picks are the ones the game made (decision #86). Never changed in place
    /// (shared between clones).
    /// </summary>
    public List<SimPick> AutoPicks = new();
    /// <summary>The biggest powered hit (blocked plus HP lost) of the play so far (Mighty Wings).</summary>
    public int PlayBiggestHit;
    /// <summary>
    /// HP loss of yours during the play so far: times some got past your block to the HP-loss modifiers (Tungsten Rod
    /// flashes when it takes 1 off), times you lost HP (UnblockedDamage > 0: Self-Forming Clay, Emotion Chip, Centennial
    /// Puzzle, Demon Tongue, Inferno, Rupture), and the HP lost (Red Skull). Not in the branch key: merged as the smallest.
    /// </summary>
    public int PlayHpReached, PlayHpLosses, PlayHpLost;
    /// <summary>
    /// What fired during the play (the play log, docs/SIMULATOR.md): source name → times (Amount) and the damage or block it
    /// caused (Data). Fired is the smallest over branches merged into this one (it happens in every one of them); FiredAny
    /// the largest (in some). Not in the branch key.
    /// </summary>
    public NamedCounts Fired = NamedCounts.Empty, FiredAny = NamedCounts.Empty;
    /// <summary>What kept you alive this turn: Fairy in a Bottle potions used, and Lizard Tail.</summary>
    [JsonIgnore] public int FairiesUsed { get => You.FairiesUsed; set => You.FairiesUsed = value; }
    [JsonIgnore] public bool LizardTailUsed { get => You.LizardTailUsed; set => You.LizardTailUsed = value; }

    /// <summary>Damage your pet Wuppy took for you (enemy attacks it redirected).</summary>
    [JsonIgnore] public int WuppyTaken { get => You.WuppyTaken; set => You.WuppyTaken = value; }
    [JsonIgnore] public int OstyTaken { get => You.OstyTaken; set => You.OstyTaken = value; }
    [JsonIgnore] public int DamageThrough { get => You.Through; set => You.Through = value; }
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

    public SimEnemy? Enemy(int id) => Enemies.FirstOrDefault(enemy => enemy.Id == id);

    [JsonIgnore]
    public IEnumerable<SimEnemy> Targetable => Enemies.Where(enemy => enemy.Hittable && enemy.Alive);

    /// <summary>
    /// The state as JSON, for reports: a report's start state can be dropped into
    /// tests/SpireQoL.Sim.Tests/Cases to replay it offline (see docs/DEVELOPMENT.md).
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, SimJson.Options);

    public static SimState FromJson(string json) => JsonSerializer.Deserialize<SimState>(json, SimJson.Options)!;

    /// <summary>
    /// Fingerprint of everything in the state (every field, via its JSON), for caching simulations
    /// between frames. Unlike Key (branch merging), it changes when anything the simulation reads
    /// changes: intents, powers, relic values, hand cards.
    /// </summary>
    public long FullKey()
    {
        var json = ToJson();
        var hash = unchecked((long)14695981039346656037UL);
        foreach (var character in json)
        {
            unchecked
            {
                hash ^= character;
                hash *= 1099511628211L;
            }
        }
        return hash;
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
    internal NamedCounts Bound = NamedCounts.Empty;
    /// <summary>The first hit of a card's attack on each enemy (enemy id → damage), for the damage-formula audit.</summary>
    internal ImmutableSortedDictionary<int, int> FirstHits = ImmutableSortedDictionary<int, int>.Empty;
    /// <summary>Card events raised inside a hit or a death, run once it's over (SimEngine.Raise, DrainPending).</summary>
    internal ImmutableArray<PendingEvent> Pending = ImmutableArray<PendingEvent>.Empty;
    /// <summary>The enemy a shot's arrow is flying at (spent before the attack, impaling after it); -1 none.</summary>
    internal int ArrowAwayAt = Who.None;
    /// <summary>The card a card event is about, when known (the played card exhausting itself: Burning Sticks).</summary>
    internal SimCard? EventCard;
    /// <summary>Card events during the current card play, for its player (Unceasing Top: nothing drawn or created).</summary>
    internal CardEventCounts PlayEvents = CardEventCounts.None;
    /// <summary>Block gains from the current card play (Unmovable doesn't count them).</summary>
    internal int PlayCardBlockGains;

    /// <summary>The listeners (SimEngine.Listeners) and the power and relic sets they were built from: rebuilt when one changes.</summary>
    internal List<(SimModel Model, int Owner)>? ListenerCache;
    internal object[]? ListenerSets;

    /// <summary>The run's work counter (SimEngine.Run sets it; every copy of the state shares it).</summary>
    internal SimWork? Work;

    /// <summary>A player by id (Who.You, Who.Teammate(k)); null for any other creature, or a teammate not in this fight.</summary>
    public SimPlayer? Player(int id) => id == Who.You ? You : Who.TeammateIndex(id) is var teammateIndex and >= 0 && teammateIndex < Teammates.Count ? Teammates[teammateIndex] : null;

    /// <summary>A player by id, to change (a teammate shared between branches is copied for this branch first).</summary>
    public SimPlayer MutablePlayer(int id) => id == Who.You ? You : MutableTeammate(Who.TeammateIndex(id));

    /// <summary>The player takes part in what their side does (you always; a teammate while alive).</summary>
    public bool TakesPart(int id) => id == Who.You || Player(id) is { Hp: > 0 };

    /// <summary>Every player's id in player order (Who.InPlayerOrder).</summary>
    [JsonIgnore] public int[] PlayerIds => Who.InPlayerOrder(Teammates.Count, TeammatesBeforeMe);

    /// <summary>Teammate k, to change: copied for this branch first if it's shared.</summary>
    public SimPlayer MutableTeammate(int teammateIndex)
    {
        if (_teammatesShared)
        {
            Teammates = Teammates.Select(player => player.Clone()).ToList();
            _teammatesShared = false;
        }
        return Teammates[teammateIndex];
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
        for (var teammateIndex = 0; teammateIndex < shown.Teammates.Count; teammateIndex++)
        {
            var player = shown.MutableTeammate(teammateIndex);
            player.TurnEndCards = new();
            player.ExhaustingInHand = new();
            player.Fading = player.HexFading = 0;
            player.Draw = null;
            player.Stampede = player.Simpleton = 0;
            player.AutoHand = null;
        }
        return shown;
    }

    /// <summary>An identical branch merged into this one: per-play results keep the guarantee (smallest) and the possibility (largest).</summary>
    public void MergeResults(SimState state)
    {
        PlayHpReached = Math.Min(PlayHpReached, state.PlayHpReached);
        PlayHpLosses = Math.Min(PlayHpLosses, state.PlayHpLosses);
        PlayHpLost = Math.Min(PlayHpLost, state.PlayHpLost);
        PlayBiggestHit = Math.Min(PlayBiggestHit, state.PlayBiggestHit);
        Fired = MergeLog(Fired, state.Fired, every: true);
        FiredAny = MergeLog(FiredAny, state.FiredAny, every: false);
    }

    /// <summary>Two play logs as one: every (the smaller of each, gone when either lacks it) or any (the larger).</summary>
    internal static NamedCounts MergeLog(NamedCounts counts, NamedCounts otherCounts, bool every)
    {
        if (ReferenceEquals(counts, otherCounts)) return counts;
        var nextCounts = every ? NamedCounts.Empty : counts;
        if (every)
        {
            foreach (var entry in counts.Items)
                if (otherCounts.Has(entry.Name)) nextCounts = nextCounts.With(entry.Name, Math.Min(entry.Amount, otherCounts[entry.Name]), Math.Min(entry.Data, otherCounts.DataOf(entry.Name)));
            return nextCounts;
        }
        foreach (var entry in otherCounts.Items) nextCounts = nextCounts.With(entry.Name, Math.Max(entry.Amount, counts[entry.Name]), Math.Max(entry.Data, counts.DataOf(entry.Name)));
        return nextCounts;
    }

    /// <summary>Logs that <paramref name="source"/> fired, with the damage or block it caused.</summary>
    /// <param name="owner">Whose source it is: an enemy's is also logged as "Name@id", so two enemies' powers stay apart.</param>
    /// <summary>Logs that <paramref name="source"/> fired, with the damage or block it caused.</summary>
    /// <param name="owner">Whose source it is: an enemy's is also logged as "Name@id", so two enemies' powers stay apart.</param>
    public void LogFired(string source, int amount = 0, int? owner = null) => LogAs(source, amount, owner, times: 1);

    /// <summary>
    /// More of what a firing gave, logged after it (each card a draw tab's source actually drew: decision #132), without counting
    /// another firing.
    /// </summary>
    public void LogAmount(string source, int amount, int? owner) => LogAs(source, amount, owner, times: 0);

    private void LogAs(string source, int amount, int? owner, int times)
    {
        // A teammate's firing is logged under them only (Name@id): its tab is theirs (decision #75), and the plain name stays
        // yours (and enemies'). An enemy's is logged under both. A firing set off by a card event is also under "~" (SimKeys).
        var kind = owner is { } id ? Who.KindOf(id) : CreatureKind.You;
        if (kind != CreatureKind.Teammate) LogUnder(source, amount, times);
        if (kind is CreatureKind.Teammate or CreatureKind.Enemy) LogUnder(SimKeys.Owned(source, owner!.Value), amount, times);
    }

    private void LogUnder(string key, int amount, int times)
    {
        Log1(key, amount, times);
        if (IndirectNow) Log1(SimKeys.Indirect(key), amount, times);
    }

    /// <summary>
    /// An indirect firing (from a card event) is also logged as "~Name" (and "~Name@id" for an enemy's or a teammate's). A
    /// teammate's firings are only under Name@id (CardPredictions.LogTab reads them there).
    /// </summary>
    private void Log1(string key, int amount, int times = 1)
    {
        Fired = Fired.With(key, Fired[key] + times, Fired.DataOf(key) + amount);
        FiredAny = FiredAny.With(key, FiredAny[key] + times, FiredAny.DataOf(key) + amount);
    }

    public SimState Clone()
    {
        if (Work != null && ++Work.Copies > Work.CopyCap) throw new SimEngine.SimCapped(); // the work cap (SimWork.MaxCopies)
        var state = (SimState)MemberwiseClone();
        state.You = You.Clone();
        _teammatesShared = true;
        state._teammatesShared = true;
        state.Enemies = new List<SimEnemy>(Enemies.Count);
        foreach (var enemy in Enemies) state.Enemies.Add(enemy.Clone());
        if (Log)
        {
            state.EndOfTurnBlock = new(EndOfTurnBlock);
            state.Blockable = new(Blockable);
            state.Unblockable = new(Unblockable);
            state.EndOfTurnDamage = new(EndOfTurnDamage);
        }
        return state;
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
        var hash = unchecked((long)14695981039346656037UL);
        void Add(long value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 1099511628211L;
            }
        }

        Add(ParryingShieldFired ? 1 : 0); Add(BlockDemand); Add(SkippedPoisoned); Add(SkippedEndOfTurn); Add(CancelledHits);
        if (DrawRecord != null)
        {
            Add(DrawRecord.Count);
            foreach (var cardHash in DrawRecord.Select(draw => draw.Card.Key.GetHashCode()).Order()) Add(cardHash);
            if (DrawRecord.Count > 0) Add((int)DrawRecord[^1].Card.Type); // Pillage reads the last one's type
        }
        Add(AutoPlaysUnknown ? 1 : 0); Add(AutoPlayed); Add(DrawnTally); Add(Unmodelled == null ? 0 : Unmodelled.GetHashCode());
        foreach (var pick in AutoPicks) { Add(pick.Card.GetHashCode()); Add(pick.Target); } // branches that picked different cards stay apart (the check tells them apart)
        foreach (var enemy in Enemies) enemy.Hash(Add);
        You.Hash(Add);
        // Teammates (shared between branches until one changes them): a branch that differs only in a teammate stays apart.
        foreach (var player in Teammates) player.Hash(Add);
        return hash;
    }
}

/// <summary>
/// What one simulation costs, the same on every machine (the performance tests limit these, not time), and what bounds it: the
/// shared memory cap, cancellation, and the background gate (SimJob). One run is one thread: plain fields, apart from the
/// shared count of states held.
/// </summary>
internal sealed class SimWork
{
    /// <summary>
    /// States all searches may hold at once (decision #107): about 2 KB each, so a few hundred MB. Past it, a search holding at
    /// least MinCapped stops where it is (SimOutcome.Truncated); smaller ones carry on (they're cheap, and a big one is the cause).
    /// </summary>
    public const long MaxHeldStates = 150_000;
    public const long MinCapped = MaxHeldStates / 8;

    /// <summary>
    /// Copies one search may make (decision #107): a guard against a search that holds little but runs on and on (a chain inside
    /// one step that branches on every branch: Beacon of Hope sharing block with Juggernaut on three or four players). About 15 s
    /// of one core; past it the search stops where it is, as at the memory cap. The heaviest fights in the tests make well under a
    /// million.
    /// </summary>
    public const long MaxCopies = 3_000_000;
    /// <summary>This search's work cap (MaxCopies; tests set their own).</summary>
    public long CopyCap = MaxCopies;
    /// <summary>Which cap stopped the search ("the memory cap", "the work cap"), or null.</summary>
    public string? CapHit;
    private static long _heldEverywhere;

    /// <summary>The states every search holds now (the cap's count).</summary>
    public static long HeldEverywhere => Interlocked.Read(ref _heldEverywhere);

    /// <summary>Steps applied to the branch set (each hit, orb, block gain, the enemy turn).</summary>
    public int Steps;
    /// <summary>Branches stepped, summed over steps.</summary>
    public int Stepped;
    /// <summary>State copies (most of the cost: every random outcome is a copy).</summary>
    public int Copies;
    /// <summary>The most copies one step made.</summary>
    public int MaxStepCopies;
    /// <summary>The most states this search held at once.</summary>
    public int PeakHeld;
    /// <summary>A cap of this search's own (tests): it stops once it holds more states than this.</summary>
    public long? OwnCap;
    private int _stepStart;
    private long _held;
    /// <summary>The states this search holds now (0 once it's over).</summary>
    public long Held => _held;

    /// <summary>Cancelled when the board changes (SimJob): checked at every step.</summary>
    public CancellationToken Cancel;
    /// <summary>Called once, at the start of the first step after HeavyAt copies (SimJob: waits for a background turn).</summary>
    public Action? OnHeavy;
    public long HeavyAt = long.MaxValue;

    /// <summary>A new step starts (BranchSet).</summary>
    public void StartStep()
    {
        Cancel.ThrowIfCancellationRequested();
        if (OnHeavy != null && Copies >= HeavyAt)
        {
            var heavy = OnHeavy;
            OnHeavy = null;
            heavy();
        }
        EndStep();
        _stepStart = Copies;
    }

    public void EndStep() => MaxStepCopies = Math.Max(MaxStepCopies, Copies - _stepStart);

    /// <summary>This search holds <paramref name="states"/> states now; false when that's past the cap (the search stops).</summary>
    public bool Hold(int states)
    {
        PeakHeld = Math.Max(PeakHeld, states);
        var total = Interlocked.Add(ref _heldEverywhere, states - _held);
        _held = states;
        var ok = OwnCap is { } own ? states <= own : total <= MaxHeldStates || states < MinCapped;
        if (!ok) CapHit ??= "the memory cap";
        return ok;
    }

    /// <summary>The search is over: what it held is free for the others.</summary>
    public void Release()
    {
        Interlocked.Add(ref _heldEverywhere, -_held);
        _held = 0;
    }
}

/// <summary>One hit of an enemy's attack intent: its base damage (AttackIntent.DamageCalc) and, per player id, the captured terms.</summary>
public sealed record IntentHit(decimal Base)
{
    public IReadOnlyDictionary<int, CapturedTerms>? Captured { get; init; }
    public CapturedTerms TermsOn(int player) => Captured != null && Captured.TryGetValue(player, out var terms) ? terms : CapturedTerms.None;
}

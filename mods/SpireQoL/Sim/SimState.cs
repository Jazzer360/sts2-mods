using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// Where the running game is main's build and main's code does a rule differently from beta's (decision #169: the adapter tells it from
/// the game's code, SimAdapter.BranchRules): the simulator follows main's way for each flag set. None (the default): beta's, every rule.
/// </summary>
[Flags]
public enum MainRule
{
    None = 0,
    /// <summary>Omnislice's splash is unpowered (Unpowered | Move) through every damage modifier, and Thorns answers it (beta: a powered hit, caps only).</summary>
    SplashIsUnpowered = 2,
    /// <summary>Juggling copies the third Attack into your hand as it's played (beta: once it's played).</summary>
    JugglingCopiesBeforeThePlay = 4,
    /// <summary>
    /// CreatureCmd.Damage tells Hook.BeforeDamageReceived (Thorns hits the dealer back) before it works out what the damage does, and each HP-loss
    /// change is told as it's made (beta, v0.114.0: the results are worked out first, CreatureCmd.GetDamageResults, then BeforeDamageReceived, then
    /// they're applied with the changes told: ApplyDamageResults).
    /// </summary>
    ReceivedBeforeTheResults = 8,
}

/// <summary>
/// Plain combat state for the outcome simulator: no game types, so the simulator can be tested on
/// its own (tests/SpireQoL.Sim.Tests). SimAdapter builds it from the live game.
/// Amounts are already through the game's modifiers where the game would apply them (damage per
/// enemy is precomputed per target; block amounts are what you'd gain).
/// </summary>
public enum OrbKind
{
    Lightning, Frost, Dark, Glass, Plasma,
    /// <summary>An orb on the board the simulator doesn't know (another mod's): it does nothing here, and the audit matches any orb.</summary>
    Unknown,
    /// <summary>
    /// A random orb to channel (OrbModel.GetRandomOrb: Chaos, The Magician, Trash to Treasure): channelling it is a branch per
    /// orb it can be (SimEngine.RandomOrbs), so no orb on the board is ever Random.
    /// </summary>
    Random,
}

/// <summary>
/// An orb in a player's slots, as its class's code keeps it (decision #214): its kind, its own fields (GlassOrb's _passiveVal, the number its passive
/// lowers), and its values as the game shows them (its PassiveVal and EvokeVal getters through Hook.ModifyOrbValue: the adapter reads them, and the
/// engine works them out again from the getters' code, ModelSpec.OrbValues, when an orb is channelled and whenever what they read changes). A value
/// whose getter is one of its fields (DarkOrb's EvokeVal is _evokeVal) is kept only as the value.
/// </summary>
public sealed class SimOrb
{
    public OrbKind Kind;
    /// <summary>Its PassiveVal as the game shows it now.</summary>
    public int Passive;
    /// <summary>Its EvokeVal as the game shows it now.</summary>
    public int Evoke;
    /// <summary>Its own fields its code reads and stores (GlassOrb's _passiveVal), but a value's own (ModelSpec.OrbAliases).</summary>
    public NamedCounts Fields = NamedCounts.Empty;

    public SimOrb Clone() => (SimOrb)MemberwiseClone();

    /// <summary>For branch keys (SimState.Key): every number of the orb.</summary>
    internal void Hash(ref BranchKey key)
    {
        key.Add((int)Kind); key.Add(Passive); key.Add(Evoke); Fields.Hash(ref key);
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
    /// <summary>
    /// Its next move is an attack (MonsterModel.IntendsToAttack: an Attack or Death Blow intent), read from the game; a stun replaces the move
    /// (CreatureCmd.Stun sets the STUNNED move), so it doesn't attack once Stunned (Go for the Eyes' test).
    /// </summary>
    public bool IntendsToAttack;
    /// <summary>Its attack this turn: damage of each hit on you (the intent's numbers).</summary>
    public List<int> Hits = new();
    /// <summary>
    /// Its attack as pipeline inputs (docs/SIMULATOR.md): each hit's base and the captured terms of unmodelled sources on each
    /// player, so Weak, Strength and the rest are applied as the hit lands. Null: Hits holds the final numbers.
    /// </summary>
    public List<IntentHit>? Intent;
    /// <summary>
    /// Its move this turn as its code runs it (decision #195: the move's program, read by the adapter), in order: its attack (MoveAttackAction:
    /// the intent's hits), the powers it applies (MovePowerAction) and the block it gives (MoveBlockAction), so what it does before a later hit
    /// (Vulnerable on you, Strength to an ally) changes that hit. Null: its intent's hits alone (a move not read whole). Fixed during a run.
    /// </summary>
    public IReadOnlyList<SimAction>? Move;
    /// <summary>Block its side's moves gave it this enemy turn (MoveBlockAction), on top of what it kept from before (Barricade).</summary>
    public int MoveBlock;
    /// <summary>
    /// The creatures its death adds to the fight (Surprise: the Gremlin Merc's Sneaky and Fat Gremlin), in the order the game adds them:
    /// SimEngine.Spawn. Null: none.
    /// </summary>
    public List<SimSpawn>? Spawns;
    /// <summary>
    /// Each hit's amount on teammate k (their own Vulnerable, Intangible, Tank...: the game's damage math per
    /// target); missing: the same as yours.
    /// </summary>
    public List<List<int>> TeammateHits = new();
    public bool Imbalanced;

    // ---- per-branch results
    /// <summary>Damage instances that got through its block during your turn (incl. end of turn).</summary>
    public int UnblockedInstances;
    /// <summary>Its first hit on you was fully blocked (Imbalanced).</summary>
    public bool FirstHitFullyBlocked;
    /// <summary>Dead before it could act (your turn, your end of turn, or its poison tick).</summary>
    public bool DiedBeforeActing;

    /// <summary>
    /// Its Poison's stacks: the power (PoisonPower, read from its code: it ticks as the enemies' turn starts), in its place among its powers.
    /// Poison above is the game's preview of their next tick, PoisonTriggers how many times they tick as read (Accelerant), for the preview.
    /// </summary>
    [JsonIgnore] public int PoisonStacks { get => Powers[PowerId.Poison]; set => Set(PowerId.Poison, value); }
    public int PoisonTriggers = 1;

    // Reactions to your card plays (block it gains during your turn; decompile v0.111.0).
    /// <summary>Curl Up: block it gains after a card whose attack damaged it (once; the power goes).</summary>
    [JsonIgnore] public int CurlUp { get => Powers[PowerId.CurlUp]; set => Set(PowerId.CurlUp, value); }
    /// <summary>Sneaky: block it gains after each Attack you play.</summary>
    [JsonIgnore] public int Sneaky { get => Powers[PowerId.Sneaky]; set => Set(PowerId.Sneaky, value); }
    /// <summary>Skittish: block it gains after a card's attack gets damage through its block (once per turn; 0 once used).</summary>
    [JsonIgnore] public int Skittish { get => Powers[PowerId.Skittish]; set => Set(PowerId.Skittish, value); }
    /// <summary>
    /// It gained block because of the play (Curl Up, Skittish, a read model's block for its enemy owner: SimEngine.EnemyBlock), for the tests and the
    /// single-player golden's fingerprint. Not in the branch key: it follows the enemy's block and its reacting power's state, which are.
    /// </summary>
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
    /// <summary>A power on it killed it during your turn (CreatureCmd.Kill of its owner: The Gambit's, as a powered attack gets HP through).</summary>
    public bool KilledByItsPower;
    /// <summary>Slippery: hits that lose it at most 1 HP each, left (0 = none).</summary>
    [JsonIgnore] public int Slippery { get => Powers[PowerId.Slippery]; set => Set(PowerId.Slippery, value); }
    /// <summary>Alive when the simulation starts (deaths after that set off Crab Rage and Ravenous).</summary>
    public bool StartedAlive = true;
    /// <summary>Ravenous: an ally dying stuns it (RavenousPower.AfterDeath).</summary>
    [JsonIgnore] public bool Ravenous { get => Powers.Has(PowerId.Ravenous); set => Set(PowerId.Ravenous, value ? 1 : 0); }

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
        RaisePoisonPreview(stacks);
        PoisonStacks += stacks;
    }

    /// <summary>The preview of its next tick (Poison) lowered by what the stacks it had (<paramref name="before"/>) dealt more than its stacks now.</summary>
    internal void FollowPoisonPreview(int before)
    {
        if (PoisonStacks == before) return;
        Poison = Math.Max(0, Poison - (SimEngine.PoisonTick(before, PoisonTriggers) - SimEngine.PoisonTick(PoisonStacks, PoisonTriggers)));
    }

    /// <summary>The preview of its next tick (Poison) raised by what <paramref name="stacks"/> more stacks add, before they land.</summary>
    internal void RaisePoisonPreview(int stacks)
    {
        if (stacks <= 0) return;
        Poison += SimEngine.PoisonTick(PoisonStacks + stacks, PoisonTriggers) - SimEngine.PoisonTick(PoisonStacks, PoisonTriggers);
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

    /// <summary>
    /// What its powers read from their code keep of their own (decision #195: a field or property each stores and reads, "Model.Field" → its
    /// value), seeded from the game and followed by their programs. Never changed in place (shared between clones).
    /// </summary>
    public NamedCounts Fields = NamedCounts.Empty;

    /// <summary>Copies the numbers; the attack list never changes during a run, so it's shared.</summary>
    public SimEnemy Clone() => (SimEnemy)MemberwiseClone();

    /// <summary>For branch keys (SimState.Key): every number that can differ between branches of one run.</summary>
    internal void Hash(ref BranchKey key)
    {
        key.Add(Id); key.Add(Hp); key.Add(Block); key.Add(UnblockedInstances); key.Add(FirstHitFullyBlocked ? 1 : 0); key.Add(Poison); key.Add(DiedBeforeActing ? 1 : 0);
        key.Add(Stunned ? 1 : 0); key.Add(IntendsToAttack ? 1 : 0); key.Add(BlockBroken ? 1 : 0); key.Add(KilledByItsPower ? 1 : 0);
        key.Add(MoveBlock);
        Powers.Hash(ref key); // Hardened Shell, Curl Up, Skittish, Burrowed, Asleep, Slumber, Flutter, Plow, The Gambit, Slippery...
        Fields.Hash(ref key);
        key.Add(Hits.Count); foreach (var hit in Hits) key.Add(hit);
    }
}

/// <summary>A card in hand that acts when its owner's turn ends: its class name, its title, and its Damage or HpLoss value.</summary>
public sealed record TurnEndCard(string Card, string Title, int Amount)
{
    /// <summary>
    /// The card itself as the hooks see it (its class, type, affliction...): the cardSource its OnTurnEndInHand passes its damage (Burn's
    /// CreatureCmd.Damage(..., this, null)). Null when it wasn't read (a test's, an older report's): a card of its class.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SimCard? Source { get; init; }

    /// <summary>Its class, title and amount, as the record printed them before it carried its card: what tells two apart (a draw profile's key).</summary>
    public override string ToString() => $"TurnEndCard {{ Card = {Card}, Title = {Title}, Amount = {Amount} }}";
}

/// <summary>
/// A creature an enemy's death adds to the fight (SimEnemy.Spawns): its name, and every HP it can start with, each equally likely
/// (Creature.SetUniqueMonsterHpValue: a value in its range no other enemy has, then ScaleMonsterHpForMultiplayer). It joins with a
/// move that does nothing this turn (the gremlins' SPAWNED_MOVE: a stun intent).
/// </summary>
public sealed record SimSpawn(string Name, IReadOnlyList<int> Hp)
{
    /// <summary>The powers it starts with (its AfterAddedToRoom's, read from its code: decision #195).</summary>
    public PowerSet Powers { get; init; } = PowerSet.Empty;
}

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
    /// The cards in their piles that listen for a hook the simulator models (SimEngine.Listeners: the game lists every card in every
    /// pile after the player's relics, potions and orbs), by class, with how many copies, in the order the game lists them (hand,
    /// draw, discard, exhaust); and the ones something on this board can make into their piles. Read by the adapter and fixed
    /// during a simulation: a card's model acts only on its own cards' events (the card drawn, the cards in hand).
    /// </summary>
    public CardSet Cards = CardSet.Empty;
    /// <summary>
    /// What they played this turn, as the hooks count it (the game's CardPlaysStarted and CardPlaysFinished), under SimKeys.Plays: "Started",
    /// "Started:Attack", "Finished:Skill", "Finished#Shiv" (by tag)... Each play of a replay counts. With them, the history ledger's other
    /// counts of this turn (decision #191: each HistoryQuery of SimState.History scoped to this turn, under its Key), seeded from the game's
    /// log and counted forward as the simulation goes; all of it starts over when the enemies' turn starts.
    /// </summary>
    public NamedCounts Turn = NamedCounts.Empty;
    /// <summary>
    /// The history ledger's counts over the whole fight (decision #191: each HistoryQuery of SimState.History scoped to the fight, under its
    /// Key), seeded from the game's log and counted forward. An entry whose Data is 1 had an event whose card wasn't read: its count isn't known.
    /// </summary>
    public NamedCounts Fight = NamedCounts.Empty;
    /// <summary>
    /// What their powers and relics read from their code keep of their own (decision #195: a field or property each stores and reads, as
    /// Kunai's AttacksPlayedThisTurn, "Model.Field" → its value), seeded from the game and followed by their programs. Never changed in place
    /// (shared between clones).
    /// </summary>
    public NamedCounts Fields = NamedCounts.Empty;

    // Powers that react or act at end of turn.
    [JsonIgnore] public int Juggernaut { get => Powers[PowerId.Juggernaut]; set => Set(PowerId.Juggernaut, value); }
    [JsonIgnore] public int Plating { get => Powers[PowerId.Plating]; set => Set(PowerId.Plating, value); }
    [JsonIgnore] public int FeelNoPain { get => Powers[PowerId.FeelNoPain]; set => Set(PowerId.FeelNoPain, value); }
    // End-of-turn powers and relics, as views onto their entries (the models read the entries: Sim/Models/EndOfTurnModels.cs).
    /// <summary>Hailstorm (amount; the Frost orbs it needs are its FrostOrbs value, read from its code).</summary>
    [JsonIgnore] public int Hailstorm { get => Powers[PowerId.Hailstorm]; set => Set(PowerId.Hailstorm, value); }
    /// <summary>The Bomb's damage when it goes off this turn end (its last tick: amount 1; Data = its damage), else 0.</summary>
    [JsonIgnore] public int ConsumingShadow { get => Powers[PowerId.ConsumingShadow]; set => Set(PowerId.ConsumingShadow, value); }
    [JsonIgnore] public int Thorns { get => Powers[PowerId.Thorns]; set => Set(PowerId.Thorns, value); }
    [JsonIgnore] public int FlameBarrier { get => Powers[PowerId.FlameBarrier]; set => Set(PowerId.FlameBarrier, value); }
    /// <summary>
    /// Inferno: each time you lose HP on your turn, this much damage to every enemy (InfernoPower.AfterDamageReceived:
    /// UnblockedDamage &gt; 0 while it's their side's turn; unpowered). Any player's (the model is the owner's).
    /// </summary>
    [JsonIgnore] public int Inferno { get => Powers[PowerId.Inferno]; set => Set(PowerId.Inferno, value); }

    // Relics (amount: their block or damage; Data where said).
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
    /// Cards in hand an upgrade could change as the turn ends: upgradable, and Ethereal or acting in hand at end of turn (Burn). A random
    /// upgrade of cards in hand (TunePower.AfterCardPlayed: TakeRandom of the upgradable ones) can't be followed while there are any
    /// (UpgradeRandomInHandStep); with none, nothing it upgrades acts before the turn is over.
    /// </summary>
    public int UpgradableAtTurnEnd;
    /// <summary>
    /// Your draw pile and discard pile by profile (decision #90): draws come from it, and what's drawn joins the hand. Null: draws
    /// are only counted (a teammate's; tests that don't set it).
    /// </summary>
    public SimDrawPile? Draw;
    /// <summary>
    /// The draw profile of each card something on this board can make into this player's draw pile (Personal Hive's Dazed, Biiig Hug's
    /// Soot), by the card's class name, as the adapter reads them for this board: SimEngine.IntoDrawPile. Null: none.
    /// </summary>
    public IReadOnlyDictionary<string, SimDrawCard>? MadeCards;
    /// <summary>The cards in their exhaust pile (Pact's End's test): read from the game, one more for each card exhausted (CardEvent.Exhausted).</summary>
    public int ExhaustPile;
    /// <summary>Cards drawn in this simulation (each drawn card's id in AutoHand).</summary>
    public int Drawn;
    /// <summary>Cards taken from your draw pile into your hand in this simulation (each one's id in AutoHand: TakenId).</summary>
    public int TakenToHand;
    /// <summary>
    /// Of those, the ones Ethereal only through Hex (HexPower.TryModifyKeywordsInCombat on its Hexed cards), and the enemy that
    /// cast it (Who.None: none). Hex is removed when its caster dies (HexPower.AfterDeath), and those cards stop being Ethereal: they
    /// stay in hand at end of turn (decision #85).
    /// </summary>
    public int HexFading;
    [JsonConverter(typeof(CreatureIdJson))]
    public int HexFrom = Who.None;
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
    /// <summary>Thunder (ThunderPower, its amount): read from its code, its AfterOrbEvoked hits each Lightning evoke's living targets again.</summary>
    [JsonIgnore] public int Thunder { get => Powers[PowerId.Thunder]; set => Set(PowerId.Thunder, value); }

    // What happens to damage reaching your HP.
    /// <summary>Tungsten Rod on the player (its code reads its HpLossReduction value: read from its code, its amount isn't read).</summary>
    [JsonIgnore] public int TungstenRod { get => Relics[RelicId.TungstenRod]; set => SetRelic(RelicId.TungstenRod, value); }

    /// <summary>Block gains so far (each sets off Juggernaut): counted for the Juggernaut tab.</summary>
    public int BlockGains;

    /// <summary>Envenom (EnvenomPower): poison on each enemy your attack gets damage through to (per hit).</summary>
    [JsonIgnore] public int Envenom { get => Powers[PowerId.Envenom]; set => Set(PowerId.Envenom, value); }
    /// <summary>Snecko Skull (relic, its amount): extra poison on every application of yours.</summary>
    [JsonIgnore] public int PoisonBonus { get => Relics[RelicId.SneckoSkull]; set => SetRelic(RelicId.SneckoSkull, value); }
    /// <summary>The Boot on the player (its code reads its DamageMinimum value: read from its code, its amount isn't read; 0 = no relic).</summary>
    [JsonIgnore] public int BootMinimum { get => Relics[RelicId.TheBoot]; set => SetRelic(RelicId.TheBoot, value); }
    /// <summary>Focus (FocusPower, its amount): read from its code, its ModifyOrbValue is in every orb value (SimOrb.Passive and Evoke).</summary>
    [JsonIgnore] public int Focus { get => Powers[PowerId.Focus]; set => Set(PowerId.Focus, value); }
    [JsonIgnore] public int Buffer { get => Powers[PowerId.Buffer]; set => Set(PowerId.Buffer, value); }
    public int OstyHp;
    /// <summary>Osty's max HP (int.MaxValue: not tracked): a heal of him stops there (CreatureCmd.Heal), a summon raises it or sets it (OstyCmd.Summon).</summary>
    public int OstyMaxHp = int.MaxValue;
    /// <summary>
    /// The player has an Osty, alive or dead (a dead Osty stays in the fight with his powers that stay: Die For You's
    /// ShouldPowerBeRemovedAfterOwnerDeath is false). False: none (none yet, or one that left the fight), so a summon adds him (OstyCmd.Summon:
    /// PlayerCmd.AddPet, Die For You).
    /// </summary>
    public bool OstyShields;
    /// <summary>
    /// Their Osty's powers, in the order applied (Die For You: he takes the powered attacks aimed at his player while he lives, read from its code,
    /// DieForYouPower.ModifyUnblockedDamageTarget): listeners at his place among the creatures (SimEngine.Listeners), his id Who.PetOf.
    /// </summary>
    public PowerSet OstyPowers = PowerSet.Empty;
    /// <summary>
    /// Their pet Wuppy's HP (Wuppy relic, v0.113.0): while it lives, its Wuppy's Loyalty takes every powered attack aimed at them, whole, before
    /// their block (WuppysLoyaltyPower.ModifyPreBlockDamageTarget, read from its code). 0: none, or dead.
    /// </summary>
    public int WuppyHp;
    /// <summary>Their Wuppy's powers, in the order applied (Wuppy's Loyalty): listeners at its place among the creatures, its id Who.PetOf.</summary>
    public PowerSet WuppyPowers = PowerSet.Empty;

    /// <summary>A pet's powers (its kind's set).</summary>
    public PowerSet PetPowers(PetKind kind) => kind == PetKind.Osty ? OstyPowers : WuppyPowers;

    /// <summary>A pet's HP (0: none, or dead).</summary>
    public int PetHp(PetKind kind) => kind == PetKind.Osty ? OstyHp : WuppyHp;

    /// <summary>
    /// HP (int.MaxValue: not tracked). Reaching 0 uses up what prevents death (Fairy in a Bottle, then Lizard Tail), each
    /// healing the player from 0; with none left, they die (Died). The same for every player (decision #93).
    /// </summary>
    public int Hp = int.MaxValue;
    /// <summary>Max HP (int.MaxValue: not tracked): a heal stops there (CreatureCmd.Heal).</summary>
    public int MaxHp = int.MaxValue;
    /// <summary>
    /// Fairy in a Bottle potions you hold (FairyInABottle.ShouldDie, checked before ShouldDieLate) and the HP each heals
    /// you to (30% of max HP, at least 1); then Lizard Tail's (50%, once: LizardTail.ShouldDieLate), 0 when used or none.
    /// </summary>
    public int Fairies, FairyHeal, LizardTailHeal;

    // ---- results, for every player (decision #93: one engine; yours are read through SimState too: Taken, Died...)
    /// <summary>HP lost (after block and the HP-loss modifiers, overkill included), damage through block, and lost by Osty.</summary>
    public int Taken, Through, OstyTaken;
    /// <summary>The part of Taken no block could stop (Regret, Beckon, Bad Luck: an Unblockable damage, SimEngine.Lands).</summary>
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
    internal void Hash(ref BranchKey key)
    {
        key.Add(Block); key.Add(Hp); key.Add(MaxHp); key.Add(Taken); key.Add(Through); key.Add(OstyTaken); key.Add(UnblockableTaken); key.Add(Incoming); key.Add(BlockGained);
        key.Add(WuppyHp); key.Add(WuppyTaken); key.Add(OstyHp); key.Add(OstyMaxHp); key.Add(OstyShields ? 1 : 0); key.Add(Died ? 1 : 0); key.Add(LizardTailUsed ? 1 : 0); key.Add(LizardTailHeal); key.Add(FairiesUsed); key.Add(Fairies);
        key.Add(BlockGains); key.Add(Energy); key.Add(HandCount); key.Add(PlayingFromHand); key.Add(Arrows); key.Add(Fading); key.Add(UpgradableAtTurnEnd); key.Add(HexFading); key.Add(HexFrom); key.Add(Drawn); key.Add(ExhaustPile); key.Add(TakenToHand);
        key.Add(OrbCapacity); key.Add(BeaconGiving ? 1 : 0); key.Add(InvincibleBlock); key.Add(InvincibleDrawPile);
        Draw?.Hash(ref key);
        key.Add(TurnEndCards.Count);
        foreach (var turnEndCard in TurnEndCards) { key.Add(turnEndCard.Card.GetHashCode()); key.Add(turnEndCard.Amount); }
        // By what each one does, in any order (copies that act the same are the same hand, as for AutoHand). Sorted only when
        // there's an order to choose: the key is made for every copy of the state.
        key.Add(ExhaustingInHand.Count);
        if (ExhaustingInHand.Count == 1) { key.Add(ExhaustingInHand[0].Energy); key.Add((int)ExhaustingInHand[0].Type); }
        else if (ExhaustingInHand.Count > 1)
            foreach (var exhausting in ExhaustingInHand.OrderBy(card => card.Energy).ThenBy(card => card.Type)) { key.Add(exhausting.Energy); key.Add((int)exhausting.Type); }
        key.Add(AutoHand?.Count ?? -1);
        // By what each card is, in any order: branches that picked identical copies hold the same hand, and merge.
        if (AutoHand != null) foreach (var cardKey in AutoHand.Select(handCard => handCard.Key).OrderBy(cardKey => cardKey.Digest)) key.Add(cardKey.GetHashCode());
        Powers.Hash(ref key); Relics.Hash(ref key); Turn.Hash(ref key); Fight.Hash(ref key); Fields.Hash(ref key); OstyPowers.Hash(ref key); WuppyPowers.Hash(ref key);
        key.Add(Orbs.Count);
        foreach (var orb in Orbs) orb.Hash(ref key);
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
        var content = JsonSerializer.SerializeToNode(this, SimJson.KeyOptions)!.AsObject();
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

    /// <summary>This card made free this turn (CardModel.SetToFreeThisTurn): its cost 0, the rest the same (its key its own).</summary>
    public SimHandCard FreeCopy() => new() { Id = Id, Name = Name, IsAttack = IsAttack, Cost = 0, Plays = Plays };

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
    /// <summary>The reason a state or play built from a member the game couldn't give is unknown (Sts2State.ReadScope).</summary>
    public static string Unread(string member) => $"the game's {member} couldn't be read (a game update?): what's built from it is a guess";

    /// <summary>You: your own player (every other player is in <see cref="Teammates"/>; any player by id: <see cref="Player"/>).</summary>
    [JsonPropertyName("P")] // the name reports and replay cases hold it under
    public SimPlayer You = new();
    public List<SimEnemy> Enemies = new();

    /// <summary>Keep the display logs (Incoming Damage's own run). Off for card simulations: faster.</summary>
    public bool Log;

    // ---- results (per branch). Yours are on your SimPlayer like every player's (decision #93); these read them.
    [JsonIgnore] public int Taken { get => You.Taken; set => You.Taken = value; }
    /// <summary>The part of Taken that no block could stop (Regret, Beckon, Bad Luck: an Unblockable damage, SimEngine.Lands), after the HP-loss modifiers.</summary>
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
    /// <summary>
    /// Where read models' code called its own Flash() (decision #199, FlashStep), under the play log's names: times flashed, smallest over
    /// merged branches (Flashed) and largest (FlashedAny), as Fired. The play check holds a read model to the game's flashes from this; the
    /// tabs read Fired, so a model that acts without flashing keeps its tab. Not in the branch key.
    /// </summary>
    public NamedCounts Flashed = NamedCounts.Empty, FlashedAny = NamedCounts.Empty;
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
    /// <summary>
    /// The payouts whose condition held where their hook ran (ModelSpec.Payouts decided where they're paid: Parrying Shield's damage as your turn
    /// ends, decision #239), each under its source's log key (SimKeys.Logged): what a tab judged on how the turn ends compares with ending it now.
    /// </summary>
    public NamedCounts Payouts = NamedCounts.Empty;
    public List<(string Name, int Amount)> EndOfTurnBlock = new();
    public List<(string Name, int Amount)> Blockable = new();
    public List<(string Name, int Amount)> Unblockable = new();
    public List<(string Name, int Amount)> EndOfTurnDamage = new();

    /// <summary>Running "blockable damage landed so far" for Imbalanced's full-block threshold.</summary>
    public int BlockDemand;

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

    /// <summary>
    /// The values among Vars that aren't whole numbers (Vulnerable's DamageIncrease 1.5, Colossus' DamageDecrease 0.5), as the game has them:
    /// Vars holds their whole part. Shared by every branch and never changed; read models read them with SimEngine.VarValue.
    /// </summary>
    public Dictionary<string, decimal> Fractions = new();

    /// <summary>The rules the running game does main's way (decision #169); None: beta's. Read once from the game, never changed.</summary>
    [JsonConverter(typeof(MainRuleJson))]
    public MainRule MainRules;

    /// <summary>
    /// The fight's multiplayer scaling (MultiplayerScalingModel.GetMultiplayerScaling(encounter, act), the game's own math, read by the adapter):
    /// what PowerModel's GetScaledAmountForMultiplayer multiplies by (decision #234). Kept among the shared values that aren't whole numbers
    /// (Fractions, under its own key), so a branch's copy holds nothing more; set once as the state is built, never changed. Null: not read (a
    /// power it would scale makes the simulation unknown).
    /// </summary>
    [JsonIgnore] public decimal? EncounterScaling
    {
        get => Fractions.TryGetValue(EncounterScalingKey, out var scaling) ? scaling : null;
        set
        {
            if (value is { } scaling) Fractions[EncounterScalingKey] = scaling;
            else Fractions.Remove(EncounterScalingKey);
        }
    }

    /// <summary>EncounterScaling's key among Fractions (no model's value has it: theirs are "owner:Model:key").</summary>
    private const string EncounterScalingKey = "fight:EncounterScaling";

    /// <summary>
    /// The players' turn number this turn (PlayerCombatState.TurnNumber: 1 on the fight's first), read from the game. Constant: a simulation
    /// never reaches the players' next turn.
    /// </summary>
    public int TurnNumber;

    /// <summary>
    /// The history ledger's questions (decision #191): every query of the game's log of the fight that the cards, relics and powers on this
    /// board ask, each player's count of it seeded by the adapter (SimPlayer.Turn, SimPlayer.Fight) and counted forward by the simulator's
    /// own events (SimEngine.Record). Shared by every branch and never changed.
    /// </summary>
    public IReadOnlyList<HistoryQuery> History = Array.Empty<HistoryQuery>();

    /// <summary>A card event is being dispatched (draw, exhaust, discard...): what fires then is an indirect proc (the Indirect procs setting).</summary>
    internal bool IndirectNow;
    /// <summary>The card event being told is an Ethereal card its owner's turn end exhausts (AfterCardExhausted's causedByEthereal: HookFact.CausedByEthereal).</summary>
    internal bool EtherealNow;

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
    /// <summary>Cards made into a player's draw pile inside a hit (Personal Hive's Dazed), put in once it's over (DrainPending).</summary>
    internal ImmutableArray<PendingMade> PendingIntoDraw = ImmutableArray<PendingMade>.Empty;
    /// <summary>Enemies whose death adds creatures (SimEnemy.Spawns), added once the hit is over (DrainPending).</summary>
    internal ImmutableArray<int> PendingSpawns = ImmutableArray<int>.Empty;
    /// <summary>Powers landed inside a hit or a death, their listeners told once it's over (SimEngine.ApplyInHit, DrainPending).</summary>
    internal ImmutableArray<PendingPower> PendingPowers = ImmutableArray<PendingPower>.Empty;
    /// <summary>Something raised inside a hit or a death waits to run (DrainPending).</summary>
    internal bool HasPending => !Pending.IsEmpty || !PendingIntoDraw.IsEmpty || !PendingSpawns.IsEmpty || !PendingPowers.IsEmpty;
    /// <summary>The damage the current attack's hits dealt, blocked, unblocked and overkill (Blossom Shot's Stretch); reset as each attack starts.</summary>
    internal int AttackDealt;
    /// <summary>The card play in progress': how many times it plays, and where the card goes after (SimEngine.DecideCardPlay); 0 and Default outside one.</summary>
    internal int PlayCountNow;
    internal SimResultLocation PlayResultLocation;
    /// <summary>
    /// What the card play in progress changed of its card (decision #218): the upgrades so far (SimEngine.UpgradeCardInPlay), its upgraded copy (the
    /// card play's, CardPlayAction.Upgraded) and the keywords added (SimEngine.KeywordCardInPlay); none outside one.
    /// </summary>
    internal CardChanges PlayChanges = CardChanges.None;
    internal int PlayUpgrades => PlayChanges.Upgrades;

    /// <summary>A card play's changes to its card (SimState.PlayChanges).</summary>
    internal readonly record struct CardChanges(int Upgrades, CardVersion? Upgraded, NamedCounts Keywords)
    {
        public static readonly CardChanges None = new(Upgrades: 0, Upgraded: null, NamedCounts.Empty);
    }
    /// <summary>The enemy a shot's arrow is flying at (spent before the attack, impaling after it); -1 none.</summary>
    internal int ArrowAwayAt = Who.None;
    /// <summary>The card a card event is about, when known (the played card exhausting itself: Burning Sticks).</summary>
    internal SimCard? EventCard;
    /// <summary>
    /// The card whose draw the AfterCardDrawn listeners are answering (from the draw pile: SimEngine.Drawn), null otherwise: Iteration
    /// reads that it's a Status, Pagestorm that it's Ethereal, Void's own hook that it's itself. A draw inside a listener's sets its own
    /// card and gives the outer one back.
    /// </summary>
    internal SimDrawCard? DrawnCard;
    /// <summary>
    /// The enemies the orb being evoked acted on (OrbModel.Evoke's targets: its hit's, each roll's own), until AfterOrbEvoked is told them
    /// (SimEngine.Evoke): branches that hit different ones stay apart until then. Null: no evoke in progress.
    /// </summary>
    internal int[]? OrbTargets;
    /// <summary>Card events during the current card play, for its player (Unceasing Top: nothing drawn or created).</summary>
    internal CardEventCounts PlayEvents = CardEventCounts.None;
    /// <summary>Block gains from the current card play (Unmovable doesn't count them).</summary>
    internal int PlayCardBlockGains;
    /// <summary>The cards the current play's last take took (TakeFromHandAction; SimCount.CardsTaken), 0 at each play's start.</summary>
    internal int PlayTaken;
    /// <summary>
    /// Through the current card play, each filtered take's cards in its owner's hand (TakeFromHandAction.Filter): set as the card play
    /// starts, then followed as cards leave and join the hand. Data 1: not known any more (a card joined whose kind wasn't read).
    /// </summary>
    internal NamedCounts TakeMatching = NamedCounts.Empty;
    /// <summary>The current play's last attack killed a creature it hit (SimTest.AttackKilled), false at each play's start and each attack's.</summary>
    internal bool AttackKilled;
    /// <summary>
    /// The answers of the card code's tests the current play asked (SimCondition.At: 1 held, 2 didn't, 3 not known), kept for the rest of the
    /// play as the code asks each once (decision #190); empty at each play's start.
    /// </summary>
    internal NamedCounts PlayTests = NamedCounts.Empty;

    /// <summary>What a card play inside another (an auto-play) puts aside and gives back: the outer play's own scratch.</summary>
    internal readonly record struct PlayFrame(SimCardPlay? Play, CardEventCounts Events, int BlockGains, int Attack, int Taken, NamedCounts Matching, bool Killed, NamedCounts Tests,
        int PlayCount, SimResultLocation Location, CardChanges Changes);

    internal PlayFrame SaveFrame() => new(CurrentPlay, PlayEvents, PlayCardBlockGains, CurrentAttack, PlayTaken, TakeMatching, AttackKilled, PlayTests, PlayCountNow, PlayResultLocation, PlayChanges);

    internal void RestoreFrame(PlayFrame frame) =>
        (CurrentPlay, PlayEvents, PlayCardBlockGains, CurrentAttack, PlayTaken, TakeMatching, AttackKilled, PlayTests, PlayCountNow, PlayResultLocation, PlayChanges) =
        (frame.Play, frame.Events, frame.BlockGains, frame.Attack, frame.Taken, frame.Matching, frame.Killed, frame.Tests, frame.PlayCount, frame.Location, frame.Changes);

    /// <summary>The listeners (SimEngine.Listeners) and the power and relic sets they were built from: rebuilt when one changes.</summary>
    internal List<(SimModel Model, int Owner)>? ListenerCache;
    internal object[]? ListenerSets;
    /// <summary>The enemies that were dead when ListenerCache was built (a bit per place in Enemies): a death rebuilds it.</summary>
    internal ulong ListenerDead;

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
            player.Fading = player.HexFading = player.UpgradableAtTurnEnd = 0;
            player.Draw = null;
            player.MadeCards = null;
            player.Cards = CardSet.Empty;
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
        Flashed = MergeLog(Flashed, state.Flashed, every: true);
        FlashedAny = MergeLog(FlashedAny, state.FlashedAny, every: false);
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
    public void LogFired(string source, int amount = 0, int? owner = null) => LogAs(source, amount, owner, times: 1);

    /// <summary>
    /// More of what a firing gave, logged after it (each card a draw tab's source actually drew: decision #132), without counting
    /// another firing.
    /// </summary>
    public void LogAmount(string source, int amount, int? owner) => LogAs(source, amount, owner, times: 0);

    /// <summary>
    /// A read model's own Flash() ran (decision #199): logged in the flash log under the play log's names (yours and an enemy's under the plain
    /// name, an enemy's and a teammate's also as "Name@id").
    /// </summary>
    /// <param name="unknown">A flash on a condition the reader doesn't read: logged under its entry's unknown key (SimKeys.UnknownFlash).</param>
    public void LogFlash(string source, int owner, bool unknown = false)
    {
        void Under(string key)
        {
            if (unknown) key = SimKeys.UnknownFlash(key);
            Flashed = Flashed.Add(key, delta: 1);
            FlashedAny = FlashedAny.Add(key, delta: 1);
        }
        var kind = Who.KindOf(owner);
        if (kind != CreatureKind.Teammate) Under(source);
        if (kind is CreatureKind.Teammate or CreatureKind.Enemy) Under(SimKeys.Owned(source, owner));
    }

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
    /// teammate's firings are only under Name@id (CardTabs.FromLog reads them there).
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
        // Every number that can differ between branches of one run, mixed into one key (BranchKey).
        // Passed by reference to each part's Hash: no delegate or closure for the key, which is made for every copy of the state.
        var key = new BranchKey();

        if (Payouts.Count > 0) Payouts.Hash(ref key);
        key.Add(BlockDemand); key.Add(SkippedPoisoned); key.Add(SkippedEndOfTurn); key.Add(CancelledHits);
        if (DrawRecord != null)
        {
            key.Add(DrawRecord.Count);
            foreach (var cardHash in DrawRecord.Select(draw => draw.Card.Key.GetHashCode()).Order()) key.Add(cardHash);
            if (DrawRecord.Count > 0) key.Add((int)DrawRecord[^1].Card.Type); // Pillage reads the last one's type
        }
        // Mid-draw (between AfterCardDrawn listeners), branches that drew different cards stay apart.
        if (DrawnCard != null) key.Add(DrawnCard.Key.GetHashCode());
        // Mid-evoke (before AfterOrbEvoked), branches whose evoke hit different enemies stay apart.
        if (OrbTargets != null) { key.Add(-OrbTargets.Length - 1); foreach (var target in OrbTargets) key.Add(target); }
        // What a take in the play took and what's left for the next (decision #189): a later play reads them.
        key.Add(PlayTaken); TakeMatching.Hash(ref key);
        // What the play's conditions read later (decision #190): the last attack's kill, the code's tests already asked.
        key.Add(AttackKilled ? 1 : 0); PlayTests.Hash(ref key);
        key.Add(PlayCountNow); key.Add((int)PlayResultLocation); // the card play's shape (SimEngine.DecideCardPlay)
        // What the play changed of the card in play (decision #218): its later plays are its upgraded copy's, or have the keywords added.
        if (PlayChanges.Upgrades != 0) key.Add(-1000 - PlayChanges.Upgrades);
        if (PlayChanges.Keywords.Count > 0) PlayChanges.Keywords.Hash(ref key);
        key.Add(AutoPlaysUnknown ? 1 : 0); key.Add(AutoPlayed); key.Add(DrawnTally); key.Add(Unmodelled == null ? 0 : Unmodelled.GetHashCode());
        foreach (var pick in AutoPicks) { key.Add(pick.Card.GetHashCode()); key.Add(pick.Target); } // branches that picked different cards stay apart (the check tells them apart)
        foreach (var enemy in Enemies) enemy.Hash(ref key);
        You.Hash(ref key);
        // Teammates (shared between branches until one changes them): a branch that differs only in a teammate stays apart.
        foreach (var player in Teammates) player.Hash(ref key);
        return key.Value;
    }
}

/// <summary>
/// A branch key being made (SimState.Key, decision #197): the numbers that can differ between branches of one run, combined in
/// order. Each number is mixed first (MurmurHash3's 64-bit finalizer, on the number plus an odd constant so 0 mixes too), so a
/// difference anywhere in it reaches every bit of what's combined: combined plainly (FNV-1a over whole numbers), a difference only
/// in high bits stays in high bits, and two of them in a row cancel (an HP past 0 is a negative number: every high bit set).
/// </summary>
public struct BranchKey
{
    private const ulong Start = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    private const ulong Odd = 0x9E3779B97F4A7C15UL;

    private ulong _hash;
    private bool _started;

    public void Add(long value)
    {
        if (!_started) { _hash = Start; _started = true; }
        unchecked { _hash = (_hash ^ Mix((ulong)value + Odd)) * Prime; }
    }

    public readonly long Value => unchecked((long)(_started ? _hash : Start));

    /// <summary>MurmurHash3's fmix64: every input bit changes about half the output bits.</summary>
    private static ulong Mix(ulong number)
    {
        unchecked
        {
            number ^= number >> 33;
            number *= 0xFF51AFD7ED558CCDUL;
            number ^= number >> 33;
            number *= 0xC4CEB9FE1A85EC53UL;
            number ^= number >> 33;
            return number;
        }
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

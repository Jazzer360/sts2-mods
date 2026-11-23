namespace SpireQoL.Sim;

/// <summary>
/// A game command the simulator mirrors (docs/CARD_PROGRAMS.md B1): the game's hooks its simulated version runs, in order, each
/// once where it first runs. A hook counts when the simulator runs it for its models, or runs a listener of it in place (Thorns'
/// BeforeDamageReceived, Juggernaut's AfterBlockGained). <see cref="Runs"/> names the simulator code that runs it.
/// </summary>
public sealed record SimPipeline(string Name, string GameCommand, string Runs, IReadOnlyList<string> Hooks);

/// <summary>
/// Every pipeline the simulator mirrors, with its hooks in the order its code runs them. PipelineParityTests (catalog tests) reads
/// each game command's Hook calls on both game branches and lists every difference (a hook missing, one extra, or one out of order);
/// each is modelled or reviewed there. A change to the code named in <see cref="SimPipeline.Runs"/> updates its row.
/// </summary>
public static class SimPipelines
{
    public static readonly IReadOnlyList<SimPipeline> All = new SimPipeline[]
    {
        new("CreatureCmd.GainBlock", "CreatureCmd.GainBlock", "SimEngine.BlockOnce, SimEngine.ProcBlockFor", new[]
        {
            "ModifyBlock",               // CalcBlock
            "AfterModifyingBlockAmount", // the sources whose term changed the amount (Vambrace, Pael's Legion)
            "AfterBlockGained",          // GainBlock (Juggernaut, Beacon of Hope), then the models
        }),
        new("CreatureCmd.Damage", "CreatureCmd.Damage", "SimEngine.Damage, after its caller's ModifiedDamage (HitOnce, ProcHitOn, RunDamage)", new[]
        {
            "ModifyDamage",                     // CalcDamage in the caller; the caps again in Damage for amounts given as final numbers
            "AfterModifyingDamageAmount",       // Slow, Hard to Kill, Intangible flash
            "BeforeDamageReceived",             // Thorns hits the dealer back
            "ModifyHpLost",                     // HpLossPipeline: BeforeOsty, BeforeOstyLate...
            "AfterModifyingHpLostBeforeOsty",   // ...the sources that changed it there...
            "AfterModifyingHpLostAfterOsty",    // ...then AfterOsty, AfterOstyLate and theirs
            "AfterBlockBroken",                 // Burrowed, Hand Drill
            "AfterDamageGiven",
            "AfterDamageReceived",
            "AfterDeath",                       // AllyDeaths, in the caller
        }),
        new("CreatureCmd.Damage on a player", "CreatureCmd.Damage", "SimEngine.EnemyTurn (TakesHit: an enemy's hit on a player)", new[]
        {
            "ModifyDamage",                     // IntentDamage, or the game's final numbers
            "BeforeDamageReceived",             // the player's Thorns
            "ModifyPreBlockDamageTarget",       // Wuppy takes it before block
            "ModifyUnblockedDamageTarget",      // Osty takes what gets through (Die For You)...
            "AfterCurrentHpChanged",            // ...and loses that HP (OstyLoses: Necro Mastery)
            "ModifyHpLost",                     // HpLoss
            "AfterModifyingHpLostBeforeOsty",
            "AfterModifyingHpLostAfterOsty",
            "ShouldDie",                        // Lose: Fairy in a Bottle, Lizard Tail...
            "AfterPreventingDeath",             // ...heal from 0
            "AfterDamageGiven",                 // PlayerDamaged
            "AfterDamageReceived",              // then Flame Barrier and Reflect
        }),
        new("AttackCommand.Execute", "AttackCommand.Execute", "SimEngine.RunAttack", new[]
        {
            "BeforeAttack",
            "AfterAttack",
        }),
        new("PowerCmd.Apply", "PowerCmd.Apply", "SimEngine.ApplyPower, SimEngine.RunApplyPower", new[]
        {
            "ModifyPowerAmountGiven",               // Snecko Skull (it flashes here), Unsettling Lamp
            "ModifyPowerAmountReceived",            // Artifact takes a debuff...
            "AfterModifyingPowerAmountReceived",    // ...and loses a charge
            "AfterPowerAmountChanged",              // AfterPowerApplied
        }),
        new("CardPileCmd.Draw", "CardPileCmd.Draw", "SimEngine.DrawOne", new[]
        {
            "ShouldDraw",           // DrawsStopped: No Draw, Fiddle
            "ModifyShuffleOrder",   // the discard pile shuffled in (SimDrawPile.Shuffled: Perfect Fit on top)...
            "AfterShuffle",         // ...and its event (CardEvent.Shuffled)
            "AfterCardDrawn",       // CardEvent.Drawn (Hellraiser first: AfterCardDrawnEarly), its listeners: Iteration, Pagestorm, Void
        }),
        new("CardCmd.Exhaust", "CardCmd.Exhaust", "SimEngine.FireCardEvent (CardEvent.Exhausted)", new[]
        {
            "AfterCardExhausted",
        }),
        new("CardCmd.Discard", "CardCmd.Discard", "SimEngine.FireCardEvent (CardEvent.Discarded)", new[]
        {
            "AfterCardDiscarded",
        }),
        new("PlayerCmd.GainEnergy", "PlayerCmd.GainEnergy", "SimEngine.CountEvent (CardEvent.EnergyGained)", Array.Empty<string>()),
        new("OrbCmd.Channel", "OrbCmd.Channel", "SimEngine.ChannelOrb", new[]
        {
            "AfterOrbEvoked",       // full slots: the front orb's evoke (Thunder, in Evoke)
            "AfterOrbChanneled",    // Metronome
        }),
    };
}

namespace SpireQoL.Sim;

/// <summary>
/// A proc tab a source's model gives a card, as the simulation decides it (CardTabs): its key ("proc:" and the source's class), crossed or
/// plain, its number and what that number counts (a block, damage, summon, draw, energy or cards-made tab: decision #132; null for a trigger),
/// the amount and the audit's range, a warning's chance (decision #159) and the aim marker (decision #55). CardPredictions gives it its icon.
/// </summary>
public sealed record SourceTab(string Key, bool Crossed, int? Number = null, TabKind? Counted = null, int Amount = 0, int AuditMin = 0, int AuditMax = 0,
    string? Chance = null, bool Aimed = false);

/// <summary>
/// Which of what the simulator works out about a source's bonus its settings row shows (decision #244): display configuration, the row's
/// (SettingsCatalog's SourceEntry.Shows), never a gate on what the simulation does. The simulator works out every direction from the code
/// (a payout's condition before and after the play, its amount, a damage term); the row picks the ones that are tabs.
/// </summary>
[Flags]
public enum TabShows
{
    None = 0,
    /// <summary>A plain tab where the play sets a payout up: off ending the turn now, on in every outcome of the play (decision #239).</summary>
    SetsUp = 1,
    /// <summary>A crossed tab where the play gives a payout up: on ending the turn now, off in some outcome (decision #239).</summary>
    GivesUp = 2,
    /// <summary>A crossed tab where the play makes a payout smaller than ending the turn now pays, or stops it (Cloak Clasp's block per card in hand).</summary>
    Reduces = 4,
    /// <summary>
    /// With <see cref="SetsUp"/>: a plain tab too where only some outcomes of the play set the payout up, carrying their chance (decision #249,
    /// the owner's: Hailstorm when Chaos might channel your first Frost). With Show chances off, set up in every outcome only, as without it.
    /// </summary>
    SetsUpByChance = 8,
    /// <summary>Every row's unless it says otherwise: both directions of a payout, as decision #239 showed them.</summary>
    Default = SetsUp | GivesUp,
}

/// <summary>
/// A source of proc tabs on the board, as CardPredictions lists them (your powers and relics, every living enemy's powers, every living
/// teammate's powers and relics), in that order: its game class, the model that stands for it, its owner in the simulation (an enemy's id, a
/// teammate's; null for yours) and which pass of the outcome rules it's in (OutcomePass: an enemy's power 0, a relic 1, a player's power 2).
/// </summary>
/// <param name="LogKind">A tab from the play log of this kind whatever the model says (the engine's own logging: Juggernaut's damage).</param>
/// <param name="Logs">It has a tab from the play log (a temporary Thorns hasn't: its Thorns has the ThornsPower tab).</param>
/// <param name="Shows">What its settings row shows of its bonus (decision #244: display configuration).</param>
public sealed record TabSource(string Class, string Model, int? Owner, int OutcomePass, TabKind? LogKind = null, bool Logs = true, TabShows Shows = TabShows.Default);

/// <summary>
/// The proc tabs of one card's simulations (decision #48, docs/SIMULATOR.md "Tabs from the log"), pure so the tests run the same rules the
/// game shows: each source whose model fired during the play (its play log), in the sources' order, then each source's rule judged on how
/// the turn ends (SimModel.OutcomeTab: enemies' powers first, then relics, then players' powers). A good effect needs every branch of some
/// aim, a warning one branch of any aim. Also what the play check holds the play to, per aim: the tabbed sources it sets off in every
/// outcome and in some (FiresBy), and the sources it reaches an unknown flash of (FlashUnknown). One per card play predicted; kept and
/// cleared between cards, so the frame allocates nothing new for it (decision #177).
/// </summary>
public sealed class CardTabs
{
    /// <summary>The tabs, in the order they're shown (a later source with the same key adds to the first's place).</summary>
    public Dictionary<string, SourceTab> Tabs { get; } = new(StringComparer.Ordinal);

    /// <summary>Per tabbed source class: whether each aim sets it off in every outcome and in some (CardPrediction.FiresEveryByAim).</summary>
    public Dictionary<string, (bool[] Every, bool[] Any)> FiresBy { get; } = new(StringComparer.Ordinal);

    /// <summary>The source classes the play reaches an unknown flash of (decision #202).</summary>
    public HashSet<string> FlashUnknown { get; } = new(StringComparer.Ordinal);

    /// <summary>For each tab, which of its aims give it (the aim marker), across every source sharing it.</summary>
    private readonly Dictionary<string, bool[]> _firesAt = new(StringComparer.Ordinal);

    /// <summary>Indirect firings (from draws, exhausts, discards, created cards, shuffles) count (the Indirect procs setting).</summary>
    public bool Indirect { get; set; }

    /// <summary>
    /// A source judged on how the turn ends that ending it now already sets off on <paramref name="owner"/> (its outcome rule has none for it there:
    /// SimModel.OutcomeTab is null where it happens anyway), so no play's tab shows it: the Play audit's reason a power went down with no tab
    /// (a stun, a wake the turn's end already makes). One with no such rule isn't.
    /// </summary>
    public static bool AlreadySetOff(SimModel model, SimState start, SimOutcome endingNow, int owner) =>
        model.JudgedOnOutcome && model.OutcomeTab(start, endingNow, owner) == null;

    /// <summary>A warning only some outcomes set off shows its chance (the Chances setting, decision #159).</summary>
    public bool Chances { get; set; }

    /// <summary>A tab with an amount shows it (the Proc numbers setting).</summary>
    public bool ProcNumbers { get; set; }

    public void Clear()
    {
        Tabs.Clear();
        FiresBy.Clear();
        FlashUnknown.Clear();
        _firesAt.Clear();
    }

    /// <summary>
    /// Every source's tabs for one card play (<paramref name="outcomes"/>: one simulation per aim, from <paramref name="start"/>), against
    /// ending the turn now (<paramref name="baseline"/>): the play log's tabs in the sources' order, then the rules judged on how the turn ends.
    /// </summary>
    public void Add(IReadOnlyList<TabSource> sources, SimState start, SimOutcome baseline, SimOutcome[] outcomes)
    {
        foreach (var source in sources)
        {
            if (source.Logs) FromLog(source, outcomes);
            SetUpFromLog(source, outcomes);
        }
        foreach (var source in sources.OrderBy(source => source.OutcomePass))
        {
            // A source listed again for the engine's own logging (LogKind) has its rules from its own entry.
            if (source.LogKind != null || SimModels.Get(source.Model) is not { JudgedOnOutcome: true } model) continue;
            if (model.OutcomeTab(start, baseline, source.Owner ?? Who.You) is { } rule) FromRule(source.Class, rule, outcomes, source.Shows);
        }
    }

    /// <summary>
    /// A source's tab from the play log: its model fired during the play, in every branch of some aim (good) or in some branch of any aim
    /// (harmful). Block, damage and summon tabs carry the amount the model logged (the most any aim guarantees) and the audit's range (the
    /// least any aim guarantees to the most any branch gives). Several enemies with the same power share one tab, their amounts added.
    /// </summary>
    private void FromLog(TabSource source, SimOutcome[] outcomes)
    {
        var model = SimModels.Get(source.Model);
        // An enemy's or a teammate's source reads its own key (SimState.LogFired: a teammate's firings are only there); yours, the plain name.
        var key = SimKeys.Logged(source.Model, source.Owner);
        // A read model's flash on a condition the reader doesn't read (an unknown flash), tab or not: the play check counts why it can't tell.
        if (model is ReadModel && outcomes.Any(outcome => outcome.PlayFlashedAny.Has(SimKeys.UnknownFlash(key)))) FlashUnknown.Add(source.Class);
        if ((source.LogKind ?? model?.Tab) is not { } kind) return;
        var harmful = model?.HarmfulFor(source.Owner ?? Who.You) ?? false; // by whose side it helps (decision #198)
        bool Fired(NamedCounts log) => log.Has(key) && (Indirect || log[key] > log[SimKeys.Indirect(key)]);
        {
            // For the audits, direct or not (the game flashes either way), whether or not a tab shows. A read model by where its code flashes
            // (the flash log, decision #199); a hand-written one by its acting log.
            var flashLog = model is ReadModel;
            var every = outcomes.Select(outcome => (flashLog ? outcome.PlayFlashedEvery : outcome.PlayFiredEvery).Has(key)).ToArray();
            var any = outcomes.Select(outcome => (flashLog ? outcome.PlayFlashedAny : outcome.PlayFiredAny).Has(key)).ToArray();
            // A flash on a condition the reader doesn't read (an unknown flash): it may flash there.
            if (flashLog)
                for (var i = 0; i < any.Length; i++)
                    if (outcomes[i].PlayFlashedAny.Has(SimKeys.UnknownFlash(key))) any[i] = true;
            Merge(source.Class, every, any);
        }
        // A payout the play decides, given up (decision #239): a tab only where its row shows that direction (decision #244).
        if (model is ReadModel { GivesUpDuringTheTurn: true } && (source.Shows & TabShows.GivesUp) == 0) return;
        Func<SimOutcome, bool> at = harmful ? outcome => Fired(outcome.PlayFiredAny) : outcome => Fired(outcome.PlayFiredEvery);
        if (!outcomes.Any(at)) return;
        // Which aims give the tab, across every source sharing it (several enemies with the same power).
        var tabKey = "proc:" + source.Class;
        var firesAt = FiresAt(tabKey, outcomes.Select(at).ToArray());
        var aimed = firesAt.Length > 1 && !firesAt.All(fires => fires);
        var amount = harmful
            ? outcomes.Max(outcome => outcome.PlayFiredAny.DataOf(key))
            : outcomes.Where(outcome => Fired(outcome.PlayFiredEvery)).Max(outcome => outcome.PlayFiredEvery.DataOf(key));
        var min = outcomes.Min(outcome => outcome.PlayFiredEvery.DataOf(key));
        var max = outcomes.Max(outcome => outcome.PlayFiredAny.DataOf(key));
        if (kind != TabKind.Trigger && max <= 0) return;
        if (Tabs.TryGetValue(tabKey, out var previous))
        {
            amount += previous.Amount;
            min += previous.AuditMin;
            max += previous.AuditMax;
        }
        int? number = kind != TabKind.Trigger && ProcNumbers ? amount : null;
        // A warning only some outcomes set off (a random hit on a Thorns enemy): its chance in the corner instead of its amount (decision
        // #159), from each branch's own log: fired in every branch merged into it (Fired) up to in some (FiredAny).
        string? chance = null;
        if (harmful && Chances) chance = WarningChance(outcomes.Where(at).Select(outcome => outcome.LoggedChance(Fired)));
        if (chance != null) number = null;
        Tabs[tabKey] = new SourceTab(tabKey, harmful, number, kind == TabKind.Trigger ? null : kind, amount, min, max, chance, aimed);
    }

    /// <summary>
    /// A payout the play decides, set up by the play (decision #244: Parrying Shield's block reaching its threshold), as its row shows it: a plain trigger
    /// where every branch of some aim turns it on (its set-up log, SimKeys.SetUp), with the aim marker where not every aim does. A warning the same
    /// source already has (it gives the payout up in some outcome) stays.
    /// </summary>
    private void SetUpFromLog(TabSource source, SimOutcome[] outcomes)
    {
        if ((source.Shows & TabShows.SetsUp) == 0 || SimModels.Get(source.Model) is not ReadModel { GivesUpDuringTheTurn: true }) return;
        var key = SimKeys.Logged(SimKeys.SetUp(source.Model), source.Owner);
        bool Fired(NamedCounts log) => log.Has(key) && (Indirect || log[key] > log[SimKeys.Indirect(key)]);
        var setsUp = outcomes.Select(outcome => Fired(outcome.PlayFiredEvery)).ToArray();
        if (!setsUp.Any(fires => fires)) return;
        var tabKey = "proc:" + source.Class;
        if (Tabs.TryGetValue(tabKey, out var had) && had.Crossed) return;
        var firesAt = FiresAt(tabKey, setsUp);
        Tabs[tabKey] = new SourceTab(tabKey, Crossed: false, Aimed: firesAt.Length > 1 && !firesAt.All(fires => fires));
    }

    /// <summary>
    /// A source's rule judged on how the turn ends (SimModel.OutcomeRule): each aim (one simulation each) passes it or not; the tab shows when
    /// some aim does, with the aim marker when not every aim does (several enemies with the same power: when no aim passes any of them). A rule
    /// that tells each branch's change to a bonus (OutcomeRule.Change) is judged on the changes the source's row shows (<paramref name="shows"/>,
    /// decision #244): none shown on its side, no tab.
    /// </summary>
    private void FromRule(string sourceClass, OutcomeRule rule, SimOutcome[] outcomes, TabShows shows)
    {
        // A bonus some outcomes set up, shown with their chance (its row's SetsUpByChance with chances on: decision #249).
        var byChance = !rule.Harmful && rule.Change != null && Chances && (shows & TabShows.SetsUpByChance) != TabShows.None;
        if (rule.Change is { } change) rule = Shown(rule, change, shows, Chances);
        var key = "proc:" + sourceClass;
        var firesAt = outcomes.Select(rule.SetOff).ToArray();
        // For the audits, per aim, as the play-log tabs are: a bonus in every outcome where it's set off, in some where some outcome sets it
        // off; a warning (set off in some outcome) in some only. Set off after the play (OutcomeRule.InPlay false): nothing for the play check
        // to see flash. A bonus by chance is held to every outcome only where every one sets it up.
        var every = rule.Harmful ? new bool[firesAt.Length]
            : byChance && rule.InBranch is { } setUp ? outcomes.Select(outcome => outcome.InEvery(setUp)).ToArray()
            : firesAt.ToArray();
        var any = outcomes.Select((outcome, index) => firesAt[index] || rule.InSome?.Invoke(outcome) == true).ToArray();
        if (rule.InPlay) Merge(sourceClass, every, any);
        if (!firesAt.Any(fires => fires)) return;
        firesAt = FiresAt(key, firesAt);
        // A warning only some outcomes set off: its chance (decision #159), the likeliest aim's; a bonus some outcomes set up, where its row
        // asks (SetsUpByChance, decision #249), the same way.
        string? chance = null;
        if ((rule.Harmful || byChance) && rule.InBranch != null && Chances) chance = WarningChance(outcomes.Where(rule.SetOff).Select(outcome => outcome.Chance(rule.InBranch)));
        Tabs[key] = new SourceTab(key, rule.Harmful, Chance: chance, Aimed: firesAt.Length > 1 && !firesAt.All(fires => fires));
    }

    /// <summary>
    /// A rule that tells each branch's change to a bonus, as its row shows it (decision #244): on its side (a warning where some outcome makes a
    /// change the row shows, with the chance of those; a bonus where every outcome does), the changes the row leaves out as if none. A side the
    /// row shows nothing of sets nothing off.
    /// </summary>
    private static OutcomeRule Shown(OutcomeRule rule, Func<SimState, TabShows> change, TabShows shows, bool chances)
    {
        var side = shows & (rule.Harmful ? TabShows.GivesUp | TabShows.Reduces : TabShows.SetsUp);
        if (side == TabShows.None) return rule with { SetOff = _ => false, InBranch = null, InSome = null, Change = null };
        bool Made(SimState branch) => (change(branch) & side) != TabShows.None;
        // A bonus some outcomes set up shows with their chance where the row asks and chances are shown (decision #249); else every outcome.
        var byChance = !rule.Harmful && chances && (shows & TabShows.SetsUpByChance) != TabShows.None;
        return rule.Harmful || byChance
            ? rule with { SetOff = outcome => !outcome.InEvery(branch => !Made(branch)), InBranch = Made, Change = null }
            : rule with { SetOff = outcome => outcome.InEvery(Made), Change = null };
    }

    /// <summary>A source's audit sets, merged with an earlier source of the same class's (several enemies with one power).</summary>
    private void Merge(string sourceClass, bool[] every, bool[] any)
    {
        if (FiresBy.TryGetValue(sourceClass, out var had) && had.Every.Length == every.Length)
            for (var i = 0; i < every.Length; i++) { every[i] |= had.Every[i]; any[i] |= had.Any[i]; }
        FiresBy[sourceClass] = (every, any);
    }

    /// <summary>Which aims give a tab, merged with what earlier sources of the same tab gave.</summary>
    private bool[] FiresAt(string tabKey, bool[] firesAt)
    {
        if (_firesAt.TryGetValue(tabKey, out var previous) && previous.Length == firesAt.Length)
            for (var i = 0; i < firesAt.Length; i++) firesAt[i] |= previous[i];
        _firesAt[tabKey] = firesAt;
        return firesAt;
    }

    /// <summary>
    /// A warning's chance on its tab (decision #159): the likeliest aim's chance that the play sets it off, as the tab's corner text ("40%",
    /// "40%?" while not every outcome is settled); null when it's certain, so the tab alone says it.
    /// </summary>
    public static string? WarningChance(IEnumerable<SimChance> perAim)
    {
        var chances = perAim.ToList();
        if (chances.Count == 0) return null;
        var likeliest = chances.MaxBy(chance => chance.High);
        return likeliest.Certain ? null : likeliest.ShortText();
    }
}

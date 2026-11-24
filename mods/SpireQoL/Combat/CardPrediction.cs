using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

/// <summary>
/// One card's prediction on one frame (decision #194): the card play the simulator was given (how many times it plays, what it costs,
/// the cards its hooks bring back), its simulations' inputs (SimInputs: the start state and each aim's play) and their results per aim
/// (each outcome, each enemy's death chance, the tabbed sources it sets off, its Frost block and evoke damage). Proc Indicators draws its
/// tabs from it, Play Preview shows its outcome, and the play check holds what happened to it: every count the check compares is its
/// simulation's (the play log: SimKeys.PlayEvent, PlayOrbChanneled, OrbEvokeBlock, OrbEvokeDamage), never a count decided on the
/// board before the play.
/// </summary>
public sealed class CardPrediction
{
    public required CardModel Card { get; init; }

    /// <summary>Times the card plays (CardPlays.Count: Echo Form, Burst, Throwing Axe...), as the simulator was given it.</summary>
    public int Plays { get; init; } = 1;

    /// <summary>Energy it costs (X-cost: all your energy) and stars (CardPlays.EnergyOf, StarsOf).</summary>
    public int Energy { get; init; }
    public int Stars { get; init; }

    /// <summary>Auto-played by an open hand choice (Decisions, Decisions): no energy is spent.</summary>
    public bool AutoPlayed { get; init; }

    /// <summary>Cards outside your hand this play brings back (Make It So, Right Hand Hand, Whistling Arrows: ReturnToHand): their tabs.</summary>
    public IReadOnlyList<CardModel> CardsBack { get; init; } = Array.Empty<CardModel>();

    /// <summary>
    /// The most cards it can transform when you choose how many (Guards: 0 or more, its pick's selector): not simulated (how many is your
    /// later decision), so the play check's created-card budget allows up to this many more. 0 for any other card.
    /// </summary>
    public int ChosenTransformsAtMost { get; init; }

    /// <summary>What the outcome simulator was given for this card on that frame (set by Proc Indicators).</summary>
    public SimInputs? Sim { get; set; }

    /// <summary>
    /// A card that acts on what it draws (decision #119): each tally its draw can give and its share of the outcomes, from the simulation its
    /// chance tab came from; null otherwise.
    /// </summary>
    public SortedDictionary<int, double>? Drawn { get; set; }

    /// <summary>
    /// The play's outcome depends on a roll: its simulation has more than one outcome or stopped at a cap, or it's aimed at a random enemy
    /// among several. The play check doesn't report what a random kill or hit could have set off.
    /// </summary>
    public bool RandomOutcome { get; set; }

    /// <summary>Its simulation was still running when the frame stopped waiting: its simulator tabs may be missing.</summary>
    public bool SimPending { get; set; }

    /// <summary>Its tabs came from a simulation stopped at a cap (decision #107): what's shown carries the ?.</summary>
    public bool SimIncomplete { get; set; }

    /// <summary>Per aim (SimInputs.PlayByTarget's key): each enemy's chance of dying before it acts after this play and the end of turn (decision #87).</summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, SimChance>>? DeathByAim { get; set; }

    /// <summary>Per aim: the whole outcome of playing it there and ending the turn (Play Preview, the play check's counts).</summary>
    public IReadOnlyDictionary<int, SimOutcome>? OutcomeByAim { get; set; }

    /// <summary>Per aim: the tabbed sources the play sets off in every outcome, and in some.</summary>
    public IReadOnlyDictionary<int, IReadOnlyCollection<Type>>? FiresEveryByAim { get; set; }
    public IReadOnlyDictionary<int, IReadOnlyCollection<Type>>? FiresAnyByAim { get; set; }
    /// <summary>
    /// The sources whose code flashes on a condition the reader doesn't read, which some outcome of the play reached (an unknown flash: may or may
    /// not): the play check doesn't hold them to a flash or its absence, and counts why.
    /// </summary>
    public IReadOnlyCollection<string> FlashUnknown { get; set; } = Array.Empty<string>();

    /// <summary>What its simulations didn't model (decision #92, rule 2): gaps the play audit counts.</summary>
    public IReadOnlyList<string> Unmodelled { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The card's own prediction when this one is the play of a card it auto-played after (Decisions, Decisions' pick: Then): the play
    /// check's counts add the two.
    /// </summary>
    public CardPrediction? Before { get; private init; }

    /// <summary>The card's prediction before its simulation: the card play as the simulator is given it.</summary>
    public static CardPrediction For(CardModel card, Player me, int plays) => new()
    {
        Card = card,
        Plays = Math.Max(1, plays),
        Energy = CardPlays.EnergyOf(card),
        Stars = CardPlays.StarsOf(card),
        AutoPlayed = CardPlays.ChoiceAutoPlayed(card),
        CardsBack = ReturnToHand.Back(card, me, plays),
        ChosenTransformsAtMost = Procs.Programs.BoardCounts.TransformsChosen(card)
            ? Procs.Programs.BoardCounts.TransformCount(card, Sts2State.Hand(me).Where(other => !ReferenceEquals(other, card)).ToList(), Sts2State.DrawPileCount(me)) : 0,
        RandomOutcome = card.TargetType == MegaCrit.Sts2.Core.Entities.Cards.TargetType.RandomEnemy
                        && card.CombatState?.HittableEnemies.Count(creature => creature.IsAlive) > 1,
    };

    /// <summary>The play's outcome at the target it was played at (its aim's simulation), when it finished without a cap; null otherwise.</summary>
    public SimOutcome? OutcomeAt(Creature? target) =>
        Sim?.AimFor(target) is { } aim && OutcomeByAim?.TryGetValue(aim, out var outcome) == true && !outcome.Truncated ? outcome : null;

    /// <summary>
    /// How many times an entry of the play log fired over the play's outcomes at the target it was played at, the fewest and the most, the
    /// card's own play before it added (Then); null when there's no finished outcome to read.
    /// </summary>
    public (int Least, int Most)? Logged(string key, Creature? target)
    {
        if (OutcomeAt(target) is not { } outcome) return null;
        var own = (outcome.PlayFiredEvery[key], outcome.PlayFiredAny[key]);
        if (Before == null) return own;
        return Before.Logged(key, target: null) is { } earlier ? (earlier.Least + own.Item1, earlier.Most + own.Item2) : null;
    }

    /// <summary>The amount a play-log entry gave over the play's outcomes at the target it was played at (Frost block, evoke damage), as Logged.</summary>
    public (int Least, int Most)? LoggedAmount(string key, Creature? target)
    {
        if (OutcomeAt(target) is not { } outcome) return null;
        var own = (outcome.PlayFiredEvery.DataOf(key), outcome.PlayFiredAny.DataOf(key));
        if (Before == null) return own;
        return Before.LoggedAmount(key, target: null) is { } earlier ? (earlier.Least + own.Item1, earlier.Most + own.Item2) : null;
    }

    /// <summary>
    /// This play followed by the plays of a card it auto-plays (Decisions, Decisions' pick, whose prediction was made during the choice): the
    /// pick's simulation (its start state already holds everything this play did before the choice), with this one's counts before it.
    /// </summary>
    internal CardPrediction Then(CardPrediction next) => new()
    {
        Card = Card, Plays = Plays, Energy = Energy, Stars = Stars, AutoPlayed = AutoPlayed,
        CardsBack = CardsBack.Concat(next.CardsBack).ToList(),
        ChosenTransformsAtMost = ChosenTransformsAtMost + next.ChosenTransformsAtMost,
        Sim = next.Sim, Drawn = next.Drawn, OutcomeByAim = next.OutcomeByAim, DeathByAim = next.DeathByAim,
        FiresEveryByAim = next.FiresEveryByAim, FiresAnyByAim = next.FiresAnyByAim, FlashUnknown = next.FlashUnknown.Count == 0 ? FlashUnknown : FlashUnknown.Count == 0 ? next.FlashUnknown : FlashUnknown.Union(next.FlashUnknown).ToList(),
        RandomOutcome = RandomOutcome || next.RandomOutcome, SimPending = SimPending || next.SimPending, SimIncomplete = SimIncomplete || next.SimIncomplete,
        Unmodelled = Unmodelled.Concat(next.Unmodelled).Distinct().ToList(),
        Before = this,
    };
}

/// <summary>The outcome simulator's inputs for one card on one frame (see CardPrediction.Sim).</summary>
public sealed class SimInputs
{
    public required SimState Start { get; init; }
    /// <summary>
    /// The same board with teammates' hands (decision #25: the checks use everything that isn't a roll), when Start leaves them
    /// out (Count teammates' hands off); null when they're the same.
    /// </summary>
    public SimState? FullStart { get; init; }

    /// <summary>
    /// Whether a teammate's hand changes the play itself (its branches, before your end of turn): then the tabs, worked out
    /// without it, can't be held to what happened, and the play's checks that rest on them aren't reported (counted instead).
    /// </summary>
    public bool HandChangesThePlay(SimPlay play)
    {
        if (FullStart == null) return false;
        SimEngine.ApplyPlayTo(Start, play, out var shown, out var shownCapped);
        SimEngine.ApplyPlayTo(FullStart, play, out var full, out var fullCapped);
        if (shownCapped || fullCapped) return true;
        // The shown run's teammates have their hand cleared: compare everything else.
        static IEnumerable<long> Keys(IEnumerable<SimState> states) => states.Select(branch => branch.WithoutTeammateHands().Key()).Distinct().OrderBy(key => key);
        return !Keys(shown).SequenceEqual(Keys(full));
    }
    public required IReadOnlyDictionary<Creature, int> Ids { get; init; }
    /// <summary>The teammates behind Start.Teammates, in order: a play aimed at one is keyed Who.Teammate(index).</summary>
    public IReadOnlyList<Creature> Allies { get; init; } = Array.Empty<Creature>();

    /// <summary>The simulation key for a play aimed at this creature: an enemy id, a teammate (Who.Teammate), or no one (Who.None).</summary>
    private int KeyFor(Creature? target) =>
        target == null ? Who.None
        : Ids.TryGetValue(target, out var id) ? id
        : Allies.ToList().IndexOf(target) is var teammateIndex and >= 0 ? Who.Teammate(teammateIndex) : Who.None;
    /// <summary>Each aim's play (an enemy id, a teammate, or Who.None for a card simulated once): looked up through AimFor.</summary>
    public Dictionary<int, SimPlay> PlayByTarget { get; } = new();

    /// <summary>
    /// The aim a play at this target was simulated under: its own, or no one's (a card simulated once, aimed at no one); null when
    /// neither was simulated. The one lookup of a played card's aim (decision #142): every check reads its play and outcomes by it.
    /// </summary>
    public int? AimFor(Creature? target) =>
        KeyFor(target) is var key && PlayByTarget.ContainsKey(key) ? key : PlayByTarget.ContainsKey(Who.None) ? Who.None : null;
}

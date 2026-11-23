using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Procs;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

public static partial class SimAdapter
{
    /// <summary>
    /// The play of <paramref name="card"/> aimed at <paramref name="targetId"/> (docs/SIMULATOR.md "Card scripts"): the card
    /// play with what each play does as actions (HP cost, attack, block, powers), worked out by the simulator from the card's
    /// values, the models and the captured terms; plus, as summary fields, what isn't an action yet (orbs, procs, hand changes).
    /// <paramref name="owner"/>: whose card it is (a teammate's auto-play: their Howl from Beyond); <paramref name="me"/> is then
    /// that teammate, and what the play gives its player goes to them.
    /// </summary>
    /// <remarks>A member it couldn't read (Sts2State.ReadScope) makes the play unknown (UnreadAction), named by the member.</remarks>
    public static SimPlay Play(CardModel card, PlayFacts facts, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int owner = Who.You)
    {
        using var reads = Sts2State.TrackReads();
        var play = PlayFromGame(card, facts, me, combat, ids, targetId, owner);
        if (reads.Unread.Count > 0) play.Before.Insert(0, new UnreadAction(reads.Unread[0]));
        return play;
    }

    /// <summary>
    /// Plays follow the card's program (decision #182). Off only in the comparison of the program's order with the one it replaced
    /// (CardProgramShadowTests).
    /// </summary>
    internal static bool UseCardPrograms = true;

    /// <summary>What a Corrupted card deals its owner on every play (Corrupted._damageAmount, a constant in its code).</summary>
    private const int CorruptedSelfDamage = 2;

    private static SimPlay PlayFromGame(CardModel card, PlayFacts facts, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int owner)
    {
        var name = card.GetType().Name;
        var plays = Math.Max(1, facts.Plays);
        var play = new SimPlay
        {
            Name = name,
            IsAttack = card.Type == CardType.Attack,
            Plays = plays,
            // A card played from elsewhere (Howl from Beyond from the exhaust pile) doesn't leave the hand.
            HandDelta = (card.Pile?.Type is PileType.Exhaust or PileType.Draw ? 0 : -1) + facts.Draws + facts.GeneratedInHand + facts.ReturnsToHand + facts.TakenToHand,
            FadingDelta = IsFading(card, combat) ? -1 : 0,
            HexFadingDelta = IsFading(card, combat) && EtherealOnlyFromHex(card) ? -1 : 0,
        };
        // Aimed at a teammate: what lands on them goes to them (never to you, even with no teammate to aim at).
        Creature? ally = null;
        var allyId = Who.None;
        if (card.TargetType == TargetType.AnyAlly && Who.TeammateIndex(targetId) is var aimedAt and >= 0)
        {
            var allies = Teammates(me, combat);
            if (aimedAt < allies.Count) { play.AllyTarget = aimedAt; ally = allies[aimedAt].Creature; allyId = Who.Teammate(aimedAt); }
        }
        play.HandCard = card.Pile?.Type == PileType.Hand ? HandId(card) : 0;
        play.Draws = facts.Draws;
        // Whom its block and channels are for, read from its code (decision #182): a teammate it's aimed at (Lift, Ignition).
        play.BlockToAlly = Procs.Programs.CardEffects.BlocksAnother(card.GetType());
        play.ChannelToAlly = Procs.Programs.CardEffects.ChannelsForTarget(card.GetType());

        // ---- the card play and what each play does
        play.TargetId = card.TargetType == TargetType.AnyAlly ? allyId : targetId;
        play.Card = new SimCardPlay(SimReaders.Card(card, facts, me), owner, play.TargetId, PlayIndex: 0, plays, facts.AutoPlayed);
        if (facts.LosesHp) play.Body.Add(new LoseHpAction(Sts2State.Var(card, "HpLoss")));
        // A shot (ArrowCmd.ShootArrowAtTarget): the arrow leaves before the attack and impales the target after it. None from an X
        // shot at X 0.
        var shot = Fool.TriesShot(card);
        if (shot) play.Body.Add(new ShootAction());
        if (facts.AttackHits > 0 && AttackOf(card, facts, me, combat, ids, targetId, plays, name) is { } attack)
            play.Body.Add((shot ? attack with { NeedsArrow = true } : attack) with { StretchFromDamage = name == nameof(BlossomShot) });
        if (shot) play.Body.Add(new ImpaleAction());
        // Recall, Uproot, Wrest: every arrow of yours out of the target.
        if (Procs.Programs.CardEffects.DislodgesArrows(card.GetType())) play.Body.Add(new DislodgeAllAction());
        if (facts.BlockGains > 0 && BlockOf(card, facts, me, combat, ids, targetId, plays, name, ally, allyId) is { } block) play.Body.Add(block);
        // Block for every player (Rally): the same value to each living teammate too, worked out on them (their Dexterity, Frail).
        if (facts.BlockGains > 0 && Procs.Programs.CardEffects.BlocksEachPlayer(card.GetType()) && card.DynamicVars.TryGetValue("Block", out var allBlock))
        {
            var mates = Teammates(me, combat);
            for (var teammateIndex = 0; teammateIndex < mates.Count; teammateIndex++)
            {
                if (mates[teammateIndex].Creature is not { IsAlive: true } mate) continue;
                play.Body.Add(new GainBlockAction(facts.BlockGains / plays, allBlock.BaseValue, SimProps.Move)
                    { Captured = SimReaders.Block(combat, card, mate, allBlock.BaseValue, ValueProp.Move), To = Who.Teammate(teammateIndex) });
            }
        }
        // Teammates' draws and created cards (Huddle Up, Blade Symphony, Constellation's draws for its target): their card
        // events, for their procs. Indirect, like your own draws and created cards.
        var drawsForTarget = Procs.Programs.CardEffects.DrawsForTarget(card.GetType());
        if (Procs.Programs.CardEffects.DrawsForEachTeammate(card.GetType()) || TeammatesShivCards.Contains(name) || (drawsForTarget && ally != null))
        {
            var mates = Teammates(me, combat);
            var cards = Sts2State.Var(card, "Cards");
            for (var teammateIndex = 0; teammateIndex < mates.Count; teammateIndex++)
            {
                if (mates[teammateIndex].Creature is not { IsAlive: true } mate) continue;
                if (drawsForTarget && mate != ally) continue;
                var who = Who.Teammate(teammateIndex);
                if (TeammatesShivCards.Contains(name))
                {
                    play.Body.Add(new CardEventAction(CardEvent.Generated, cards) { Player = who, Indirect = true });
                    continue;
                }
                // Their draws: as many as their hand has room for and their piles hold; a shuffle when the draw pile runs out.
                var hand = PileType.Hand.GetPile(mates[teammateIndex]).Cards.Count;
                var drawPile = PileType.Draw.GetPile(mates[teammateIndex]).Cards.Count;
                var discard = PileType.Discard.GetPile(mates[teammateIndex]).Cards.Count;
                var drawn = Math.Min(cards, Math.Min(Math.Max(0, CardPile.MaxCardsInHand - hand), drawPile + discard));
                if (drawn > drawPile) play.Body.Add(new CardEventAction(CardEvent.Shuffled, Count: 1) { Player = who, Indirect = true });
                if (drawn > 0) play.Body.Add(new CardEventAction(CardEvent.Drawn, drawn) { Player = who, Indirect = true });
            }
        }
        if (Procs.Programs.CardEffects.TakesTargetsBlock(card.GetType())) play.Body.Add(new StripBlockAction(RemoveArtifact: true));
        play.Body.AddRange(PowersOf(card, facts, name, allyId));
        if (TriggerPoisonCards.Contains(name)) play.Body.Add(new TriggerPoisonAction());
        play.Body.AddRange(OrbsOf(card, me, combat, name, play.ChannelToAlly ? allyId : owner));
        // The card's own energy (decision #117: Production, Sunder's kill, Double Energy...), each play.
        CardEnergy.AddTo(play, card, me, combat, ids.FirstOrDefault(entry => entry.Value == targetId).Key, owner, allyId);
        // Stars that wait on the attack's kill (Knockout Blow): the attack's, when its target dies.
        if (Procs.Programs.CardEffects.GainsStarsOnKill(card.GetType()))
            for (var i = 0; i < play.Body.Count; i++)
                if (play.Body[i] is AttackAction attackAction) { play.Body[i] = attackAction with { KillRewards = attackAction.KillRewards.Append(new KillReward(CardEvent.StarsGained, Count: 1)).ToList() }; break; }
        play.Body.AddRange(TakesToHand(facts, me));
        AddCardEvents(play, facts, plays);
        // Reboot: your hand into your draw pile and a shuffle (CardPileCmd.Shuffle) before its draws, right after the hand leaves.
        if (Procs.Programs.CardEffects.ShufflesDiscardPileIn(card.GetType()) && play.Body.FindIndex(action => action is LeaveHandAction { Whole: true }) is var leaves and >= 0)
            play.Body.Insert(leaves + 1, new ShuffleDrawPileAction(PileType.Hand.GetPile(me).Cards.Where(other => other != card)
                .Select(other => DrawProfiles.GetValueOrDefault(other)).OfType<SimDrawCard>().GroupBy(profile => profile.Key)
                .Select(sameProfile => new SimDrawGroup(sameProfile.First(), sameProfile.Count())).ToList()));
        // Cards it auto-plays from the top of your draw pile, after what it does to your hand (The Devil exhausts it first).
        if (Procs.Programs.CardEffects.AutoPlaysFromDrawPile(card.GetType())) play.Body.Add(new AutoPlayFromDrawAction(name switch
        {
            nameof(Havoc) => 1,
            nameof(Cascade) => SafeInt(() => Hook.ModifyXValue(combat, card, card.EnergyCost.GetAmountToSpend())) + (card.IsUpgraded ? 1 : 0),
            _ => PileType.Hand.GetPile(me).Cards.Count(other => other != card), // The Devil: the cards in your hand as it plays
        }) { ForceExhaust = name == nameof(Havoc), Upgrade = name == nameof(TheDevil) && card.IsUpgraded });
        // A replay of a card whose numbers change with each play in a way its code doesn't show (decision #160: Thrash).
        if (plays > 1 && PlayGrowth.Of(card).Unmodelled is { } growthUnknown) play.Body.Add(new NotModelledAction(growthUnknown));
        if (DrawnCardReaders.ContainsKey(name)) UseDrawnCard(play, name);

        // ---- summary fields (not actions yet)
        play.ByOsty = facts.ByOsty;
        // The rest of what each play does, after the card's own actions above: Capture Spirit's damage, Judgement, Bouncing Flask's
        // poison on random enemies, Osty's death (Sacrifice, Bone Shards; Necro Mastery reacts).
        var target2 = ids.FirstOrDefault(entry => entry.Value == targetId).Key;
        if (card.Type != CardType.Attack && Procs.Programs.CardEffects.DamagesTargetWithoutAttacking(card.GetType()) && target2 != null && card.DynamicVars.TryGetValue("Damage", out var tdVar) && tdVar is DamageVar tdv)
            play.Body.Add(new DamageAction(new DamageSpec(tdv.BaseValue, SimReaders.Props(tdv.Props))
                { Captured = new Dictionary<int, CapturedTerms> { [targetId] = SimReaders.Damage(me.RunState, combat, card, me.Creature, target2, tdv.BaseValue, tdv.Props) } }));
        if (Procs.Programs.CardEffects.SetsTargetsHp(card.GetType())) play.Body.Add(new ExecuteAction(Sts2State.Var(card, "Threshold")));
        if (card.TargetType == TargetType.RandomEnemy && card.DynamicVars.Values.FirstOrDefault(IsPowerVar<PoisonPower>) is { } flask)
            play.Body.Add(new ApplyPowerAction(PowerId.Poison, (int)flask.BaseValue)
                { To = ApplyPowerAction.Where.RandomEnemy, Times = Math.Max(1, Sts2State.Var(card, "Repeat", fallback: 1)), Debuff = true });
        if (Procs.Programs.CardEffects.KillsOsty(card.GetType())) play.Body.Add(new KillOstyAction());
        // What the card does, in the order its code does it (decision #182: its program).
        if (UseCardPrograms) play.Body = PlayOrder.Arrange(play.Body, PlayOrder.ProgramOf(card.GetType()));
        // The card's enchantment acts after the card itself, on every play (CardModel: EnchantmentModel.OnPlay after OnPlay). The
        // coverage ledger reviews every enchantment's OnPlay.
        switch (card.Enchantment)
        {
            // Adroit: its block (EnchantmentModel.OnPlay → CreatureCmd.GainBlock).
            case MegaCrit.Sts2.Core.Models.Enchantments.Adroit adroit:
                var baseValue = adroit.DynamicVars.Block.BaseValue;
                play.Body.Add(new GainBlockAction(Times: 1, baseValue, SimProps.Move) { Captured = SimReaders.Block(combat, card, me.Creature, baseValue, ValueProp.Move) });
                break;
            // Corrupted: damage to its owner, unblockable (Corrupted._damageAmount): your HP-loss modifiers and the damage hooks hear it
            // (Centennial Puzzle).
            case MegaCrit.Sts2.Core.Models.Enchantments.Corrupted when owner == Who.You:
                play.Body.Add(new LoseHpAction(CorruptedSelfDamage));
                break;
            case MegaCrit.Sts2.Core.Models.Enchantments.Corrupted:
                play.Body.Add(new NotModelledAction("a teammate's Corrupted card hurts them"));
                break;
            // Inky: Weak on the card's target, or on every enemy for a card that hits them all.
            case MegaCrit.Sts2.Core.Models.Enchantments.Inky inky when inky.DynamicVars.Values.FirstOrDefault(IsPowerVar<WeakPower>) is { } weak:
                play.Body.Add(new ApplyPowerAction(PowerId.Weak, (int)weak.BaseValue)
                    { To = card.TargetType == TargetType.AllEnemies ? ApplyPowerAction.Where.AllEnemies : ApplyPowerAction.Where.Target, Debuff = true });
                break;
        }
        // A teammate's card: the block it gives its player is theirs.
        if (owner != Who.You)
            for (var i = 0; i < play.Body.Count; i++)
                if (play.Body[i] is GainBlockAction { To: Who.You } gainBlockAction) play.Body[i] = gainBlockAction with { To = owner };
        return play;
    }

    /// <summary>
    /// A card that acts on what its own draw gave (decision #118: DrawnCardReaders): its draw and its reaction become one
    /// DrawnCardAction in the draw's place, so the reaction sees the card each branch drew. Escape Plan's block moves into it (the game
    /// gains it after the draw, only for a Skill); Scrape's discards are the ones it makes there, not the plain count.
    /// </summary>
    private static void UseDrawnCard(SimPlay play, string name)
    {
        var at = play.Body.FindIndex(simAction => simAction is CardEventAction { Event: CardEvent.Drawn });
        if (at < 0) return; // nothing drawn (a full hand, empty piles): nothing to act on
        var draw = (CardEventAction)play.Body[at];
        var action = new DrawnCardAction(draw.Count) { Indirect = draw.Indirect };
        switch (name)
        {
            case nameof(EscapePlan):
                if (play.Body.OfType<GainBlockAction>().FirstOrDefault() is { } block)
                {
                    play.Body.Remove(block);
                    action = action with { BlockIf = SimCardType.Skill, Block = block };
                }
                break;
            case nameof(Pillage):
                action = action with { DrawAgainIf = SimCardType.Attack };
                break;
            case nameof(Scrape):
                play.Body.RemoveAll(simAction => simAction is CardEventAction { Event: CardEvent.Discarded });
                action = action with { DiscardUnlessFree = true };
                break;
        }
        play.Body[play.Body.FindIndex(simAction => simAction is CardEventAction { Event: CardEvent.Drawn })] = action;
    }

    /// <summary>
    /// The card events the play causes (PlayFacts' counts), for the models that react to them: the energy it spends and gives back
    /// (before the play), each play's draws, discards, other exhausts, created cards and star gains (indirect, but star gains), the
    /// stars it spends (first play), its own exhaust and a shuffle (after).
    /// </summary>
    private static void AddCardEvents(SimPlay play, PlayFacts facts, int plays)
    {
        void Each(List<SimAction> to, CardEvent cardEvent, int total, bool indirect, bool perPlay = true)
        {
            var perStep = perPlay ? total / plays : total;
            if (perStep > 0) to.Add(new CardEventAction(cardEvent, perStep) { Indirect = indirect });
        }
        Each(play.Before, CardEvent.EnergyGained, facts.EnergyBack, indirect: false, perPlay: false);
        Each(play.Before, CardEvent.EnergySpent, facts.EnergySpent, indirect: false, perPlay: false);
        // What it takes from your hand (discards, exhausts) and its draws: PlayOrder puts them in its code's order (decision #182),
        // which decides the room the draws have.
        void Leaving(List<SimAction> into)
        {
            // Which cards it takes (what those that act when exhausted do: Drum of Battle), read from the hand as it is.
            if (facts.ExhaustsOthers > 0 && PlayFacts.ExhaustPickOf(facts.Card) is { } exhaustPick) into.Add(exhaustPick);
            // Then they leave, before what their leaving sets off: the game takes them one at a time, each draw it sets off (Dark
            // Embrace, Tough Bandages) following a card out, so those draws have its room, and a play that takes the whole hand takes
            // what was in it, not what the draws bring in (a dev review: Fiend Fire with Dark Embrace drew 7, predicted 2).
            if (facts.TakesWholeHand) into.Add(new LeaveHandAction(Count: 0) { Whole = true });
            else if (facts.LeavesHand / plays > 0) into.Add(new LeaveHandAction(facts.LeavesHand / plays));
            Each(into, CardEvent.Discarded, facts.Discards, indirect: true);
            Each(into, CardEvent.Exhausted, facts.ExhaustsOthers, indirect: true);
            // A Sly card it discards without a choice (the whole hand: CardCmd.Discard's AutoPlay, SlyDiscard) plays itself: not
            // simulated yet (tracker). One you pick is a card, never a particular one (decision #92): your own decision, neither
            // simulated nor marked (PlayFacts.MayDiscardSly leaves picks out).
            if (facts.MayDiscardSly) into.Add(new NotModelledAction("a Sly card in your hand it discards plays itself"));
            // How many it takes into your hand is your choice (Neow's Fury): your hand after it isn't known.
            if (facts.HandTakeChosen) into.Add(new NotModelledAction("you choose how many cards it takes into your hand"));
        }
        Leaving(play.Body);
        Each(play.Body, CardEvent.Drawn, Math.Min(facts.OwnDraws, facts.Draws), indirect: true);
        var sadness = facts.SadnessCreated;
        var statuses = Math.Max(0, facts.GeneratedStatuses - sadness);
        Each(play.Body, CardEvent.Generated, Math.Max(0, facts.Generated + facts.TransformsCertain - statuses - sadness), indirect: true);
        Each(play.Body, CardEvent.GeneratedStatus, statuses, indirect: true);
        Each(play.Body, CardEvent.GeneratedSadness, sadness, indirect: true);
        Each(play.Body, CardEvent.StarsGained, facts.StarGains, indirect: false);
        // Spent with the energy, before the card does anything (PlayCardAction: SpendResources, then OnPlay): the play's first action,
        // so Mini Regent's Strength is on the card's own hits. Inside the play, as Galactic Dust and Child of the Stars read its stars.
        if (facts.Stars > 0) play.Body.Insert(0, new CardEventAction(CardEvent.StarsSpent, Count: 1) { FirstPlayOnly = true });
        if (facts.ExhaustsSelf) play.After.Add(new CardEventAction(CardEvent.Exhausted, Count: 1) { Card = play.Card!.Card });
        // Its own exhaust (Corruption: Skills exhaust) sets off its own AfterCardExhausted (Drum of Battle's energy).
        if (facts.ExhaustsSelf && CardEnergy.WhenExhausted(facts.Card) is > 0 and var ownExhaustEnergy)
            play.After.Add(new CardEventAction(CardEvent.EnergyGained, ownExhaustEnergy) { Player = play.Card!.Owner });
        if (facts.Shuffles) play.After.Add(new CardEventAction(CardEvent.Shuffled, Count: 1) { Indirect = true });
    }

    private static bool IsPowerVar<T>(DynamicVar dynamicVar) where T : PowerModel =>
        dynamicVar.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(PowerVar<>) && type.GetGenericArguments()[0] == typeof(T);

    /// <summary>
    /// The card's attack, each play: its hits per play, its damage value (calculated on each target when it's a calculated
    /// value) and props, and the captured terms on each enemy. Osty's attacks keep the game's final numbers (his own powers
    /// aren't simulated). Timber's kill attack and Rain Shot's splash ride along.
    /// </summary>
    private static AttackAction? AttackOf(CardModel card, PlayFacts facts, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int plays, string name)
    {
        var mode = card.TargetType switch
        {
            TargetType.AnyEnemy => SimPlay.Target.Single,
            TargetType.AllEnemies => SimPlay.Target.All,
            TargetType.RandomEnemy => SimPlay.Target.Random,
            _ => SimPlay.Target.None,
        };
        if (mode == SimPlay.Target.None) return null;
        var aimed = ids.FirstOrDefault(entry => entry.Value == targetId).Key;
        var hits = (mode == SimPlay.Target.Single && aimed != null ? facts.HitsOn(aimed) : facts.AttackHits) / plays;
        if (hits <= 0) return null;

        var baseOn = new Dictionary<int, decimal>();
        var captured = new Dictionary<int, CapturedTerms>();
        decimal baseDamage = 0m;
        var props = ValueProp.Move;
        CalculatedVar? calc = null;
        if (card.DynamicVars.TryGetValue("CalculatedDamage", out var dynamicVar) && dynamicVar is CalculatedVar calculatedVar)
        {
            calc = calculatedVar;
            if (dynamicVar is CalculatedDamageVar cdv) props = cdv.Props;
        }
        else if ((card.DynamicVars.TryGetValue("Damage", out var otherDynamicVar) || card.DynamicVars.TryGetValue("OstyDamage", out otherDynamicVar)) && otherDynamicVar is DamageVar damageVar)
        {
            baseDamage = damageVar.BaseValue;
            props = damageVar.Props;
        }
        var fixedNumbers = facts.ByOsty || (calc == null && baseDamage == 0m);
        foreach (var (creature, id) in ids)
        {
            if (!facts.CanHit(creature)) continue;
            if (fixedNumbers)
            {
                baseOn[id] = facts.DamagePerHit(creature);
                continue;
            }
            var baseValue = calc != null ? SafeDecimal(() => calc.Calculate(creature)) : baseDamage;
            baseOn[id] = baseValue;
            captured[id] = SimReaders.Damage(me.RunState, combat, card, me.Creature, creature, baseValue, props);
        }
        DamageSpec? onKill = null;
        if (Procs.Programs.CardEffects.HitsAllOnKill(card.GetType()) && card.DynamicVars.TryGetValue("OnKillDamage", out var kv2) && kv2 is DamageVar kdv)
        {
            var killTerms = new Dictionary<int, CapturedTerms>();
            foreach (var (creature, id) in ids)
                if (creature.IsAlive && creature.IsHittable) killTerms[id] = SimReaders.Damage(me.RunState, combat, card, me.Creature, creature, kdv.BaseValue, kdv.Props);
            onKill = new DamageSpec(kdv.BaseValue, SimReaders.Props(kdv.Props)) { Captured = killTerms };
        }
        var growth = plays > 1 && !fixedNumbers ? PlayGrowth.Of(card) : PlayGrowth.Growth.None;
        return new AttackAction(hits, mode, new DamageSpec(baseDamage, SimReaders.Props(props))
            { BaseOn = baseOn, Captured = captured, Fixed = fixedNumbers, GrowsEachPlay = growth.Damage, EnchantFirstPlayOnly = growth.EnchantFirstPlayOnly })
        {
            ByOsty = facts.ByOsty, // a single target: the play's (SimPlay.TargetId)
            OnKill = onKill,
            Splash = PlayFacts.SplashCards.Contains(name), // the target's damage to every other enemy
            RepeatPerKill = RepeatPerKillCards.Contains(name),
        };
    }

    /// <summary>
    /// The card's own block, each play: its block value through the block pipeline (models and captured terms), to you or the
    /// targeted teammate. Fisticuffs' block is the damage it dealt, a powered Move through the pipeline (Dexterity, Frail).
    /// Other cards whose block isn't a value (Entrench: your block; Mimic and a calculated block aimed at a teammate) keep the
    /// game's number.
    /// </summary>
    private static GainBlockAction? BlockOf(CardModel card, PlayFacts facts, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids,
        int targetId, int plays, string name, Creature? ally, int allyId)
    {
        var gains = facts.BlockGains / plays;
        if (gains <= 0) return null;
        var toAlly = Procs.Programs.CardEffects.BlocksAnother(card.GetType());
        var to = toAlly ? allyId : Who.You;
        if (toAlly && allyId == Who.None) return null; // no teammate to aim at: the block goes to no one
        var aimed = ids.FirstOrDefault(entry => entry.Value == targetId).Key;
        if (name == nameof(Fisticuffs))
        {
            // Fisticuffs.OnPlay: CreatureCmd.GainBlock(the damage dealt, ValueProp.Move), so Dexterity and Frail apply.
            var dealt = aimed != null ? facts.TotalDamage(aimed) / plays : 0;
            return new GainBlockAction(gains, dealt, SimProps.Move) { Captured = SimReaders.Block(combat, card, me.Creature, dealt, ValueProp.Move), To = to };
        }
        int? fixedEach = name switch
        {
            nameof(Entrench) => me.Creature.Block,
            _ when toAlly && card.DynamicVars.ContainsKey("CalculatedBlock") => ally != null ? CardBlock(card, combat, me, ally, ally) : 0,
            nameof(Mimic) when ally != null => CardBlock(card, combat, me, me.Creature, ally),
            _ => null,
        };
        if (fixedEach is { } each) return new GainBlockAction(gains, each, SimProps.Move) { Fixed = true, To = to };
        decimal baseValue;
        if (card.DynamicVars.TryGetValue("CalculatedBlock", out var dynamicVar) && dynamicVar is CalculatedVar calc) baseValue = SafeDecimal(() => calc.Calculate(null));
        else if (card.DynamicVars.TryGetValue("Block", out var otherDynamicVar)) baseValue = otherDynamicVar.BaseValue;
        else return new GainBlockAction(gains, CardBlock(card, combat, me), SimProps.Move) { Fixed = true, To = to };
        var gainer = toAlly && ally != null ? ally : me.Creature;
        var growth = plays > 1 ? PlayGrowth.Of(card) : PlayGrowth.Growth.None;
        return new GainBlockAction(gains, baseValue, SimProps.Move)
        {
            Captured = SimReaders.Block(combat, card, gainer, baseValue, ValueProp.Move), To = to, GrowsEachPlay = growth.Block,
            EnchantFirstPlayOnly = growth.EnchantFirstPlayOnly,
        };
    }

    /// <summary>
    /// The powers the card's code applies (CardCoverage.PowersApplied), each play: buffs to you (or the targeted teammate:
    /// Intercept's Covered), the debuffs PlayFacts finds it lands on enemies to its target or every enemy, and the Strength it
    /// takes or gives (EnemyStrengthCards). Amounts from the card's PowerVar, its plain value, or 1. Only powers the simulator
    /// reads: modelled ones, the ones it keeps as numbers, and poison.
    /// </summary>
    private static IEnumerable<SimAction> PowersOf(CardModel card, PlayFacts facts, string name, int allyId)
    {
        var actions = new List<SimAction>();
        int AmountOf(Type type)
        {
            foreach (var dynamicVar in card.DynamicVars.Values)
            {
                var otherType = dynamicVar.GetType();
                if (otherType.IsGenericType && otherType.GetGenericTypeDefinition() == typeof(PowerVar<>) && otherType.GetGenericArguments()[0] == type) return (int)dynamicVar.BaseValue;
            }
            return PlainSelfPowerAmount(card, type) ?? 1;
        }
        bool Simulated(string powerName) => SimModels.Has(powerName) || SelfPowers.Contains(powerName) || powerName == nameof(PoisonPower) || powerName == nameof(ArtifactPower);
        var debuffs = facts.EnemyDebuffs.Concat(facts.EnemyTemporaryDebuffs).ToHashSet();
        var whereEnemies = card.TargetType switch
        {
            TargetType.AnyEnemy => ApplyPowerAction.Where.Target,
            TargetType.AllEnemies => ApplyPowerAction.Where.AllEnemies,
            _ => (ApplyPowerAction.Where?)null,
        };
        foreach (var type in Diagnostics.CardCoverage.PowersApplied(card.GetType()))
        {
            var simPowerName = SimPowerName(type);
            if (!Simulated(simPowerName) && !(PlayFacts.IsDebuffType(type) && debuffs.Contains(type))) continue;
            if (simPowerName == nameof(PoisonPower) && card.TargetType == TargetType.RandomEnemy) continue; // Bouncing Flask: random, a summary field
            // A temporary Strength loss (Piercing Wail, Dark Shackles: TemporaryStrengthPower) is the Strength action below
            // (EnemyStrengthCards): one debuff, so one Artifact charge takes all of it (issue #371: applied twice, Artifact took
            // the first and the Strength still landed).
            if (typeof(TemporaryStrengthPower).IsAssignableFrom(type) && EnemyStrengthCards.ContainsKey(name)) continue;
            if (PlayFacts.IsDebuffType(type))
            {
                if (!debuffs.Contains(type) || whereEnemies is not { } where) continue;
                actions.Add(new ApplyPowerAction(new PowerId(simPowerName), AmountOf(type)) { To = where, Debuff = true });
            }
            else if (card.TargetType == TargetType.AnyAlly && allyId != Who.None && simPowerName == nameof(CoveredPower))
            {
                actions.Add(new ApplyPowerAction(new PowerId(simPowerName), AmountOf(type)) { To = ApplyPowerAction.Where.Creature, Creature = allyId });
            }
            else if (simPowerName == nameof(StretchPower) && Procs.Programs.CardEffects.KeepsStretchForReplays(card.GetType()))
            {
                // Cho Ki: your Stretch again, as much as you have (so the play keeps it).
                actions.Add(new ApplyPowerAction(new PowerId(simPowerName), Amount: 0) { To = ApplyPowerAction.Where.You, CopiesOwn = true });
            }
            else
            {
                actions.Add(new ApplyPowerAction(new PowerId(simPowerName), AmountOf(type)) { To = ApplyPowerAction.Where.You });
            }
        }
        // Strength the card takes from enemies (this turn or for good) or gives them (Fight Me).
        if (EnemyStrengthCards.TryGetValue(name, out var str) && str.Var != null)
        {
            var amount = str.Sign * (str.Var == "" ? 1 : Sts2State.Var(card, str.Var));
            var where = str.All ? ApplyPowerAction.Where.AllEnemies : ApplyPowerAction.Where.Target;
            actions.Add(new ApplyPowerAction(PowerId.Strength, amount) { To = where, Debuff = amount < 0 });
        }
        return actions;
    }

    /// <summary>How a card's play takes cards into your hand (HandTakeOf).</summary>
    /// <param name="Piles">The piles it takes them from (your draw pile, your discard pile; both for a collection over every pile).</param>
    /// <param name="Eligible">Which cards there it can take.</param>
    /// <param name="Random">The pick is a roll (TakeRandom); else it's yours.</param>
    /// <param name="AllAtOnce">Every card it takes, on its first play (PlayFacts.TakenToHand); else one a play.</param>
    internal sealed record HandTake(IReadOnlyList<PileType> Piles, Func<CardModel, bool> Eligible, bool Random = false, bool AllAtOnce = false);

    /// <summary>
    /// How the card's play takes cards into your hand, read from its program (CardEffects.TakesIntoHandOf: its CardPileCmd.Add to the
    /// hand), so the cards it takes leave their pile in the simulation (TakeToHandAction): a pick from a pile, of the cards its filter
    /// lets through (Secret Weapon's Attacks), one a play unless it picks more (Dredge); a roll from a collection (Anointed's Rares, The
    /// World's discard pile), or each card of one (All For One's 0-cost cards, Summon Forth's Sovereign Blades from every pile), all on
    /// the first play. Null when it takes none, or you choose how many (Neow's Fury), or what it takes from can't be read.
    /// </summary>
    internal static HandTake? HandTakeOf(CardModel card)
    {
        var takes = Procs.Programs.CardEffects.TakesIntoHandOf(card.GetType());
        if (takes.Count == 0 || takes.Any(take => take.ChoosesHowMany)) return null;
        var take = takes[0];
        var program = Procs.Programs.CardPrograms.Of(card.GetType());
        var board = new Procs.Programs.ProgramBoard(card);
        switch (take.Way)
        {
            case Procs.Programs.CardEffects.TakeWay.Picked when take.PickPile is { } pile && pile != PileType.Hand:
                return new HandTake(new[] { pile }, other => (take.PickFilter is not { } filter || Procs.Programs.ProgramEvaluator.Passes(filter, other, board, program) != false)
                                                             && Procs.Programs.ProgramEvaluator.HoldsFor(take.ItemTest, other, board, program) != false,
                    AllAtOnce: take.PerPick is not Procs.Programs.ConstantValue { Value: 1 });
            case Procs.Programs.CardEffects.TakeWay.Random or Procs.Programs.CardEffects.TakeWay.Each when take.Items != null
                                                                                                       && Procs.Programs.ProgramEvaluator.Items(take.Items, board, program) is { } items:
            {
                var cards = items.OfType<CardModel>().Where(other => !ReferenceEquals(other, card)).ToHashSet(ReferenceEqualityComparer.Instance);
                // The piles its collection is over (a pile's cards, or every pile: All Cards).
                var piles = take.Items.Parts().OfType<Procs.Programs.QueryValue>().Where(value => value is { Name: "GetPile", Arguments: [Procs.Programs.ConstantValue, ..] })
                    .Select(value => (PileType)((Procs.Programs.ConstantValue)value.Arguments[0]).Value).Distinct().ToList();
                if (piles.Count == 0) piles = new List<PileType> { PileType.Draw, PileType.Discard };
                return new HandTake(piles, other => cards.Contains(other), Random: take.Way == Procs.Programs.CardEffects.TakeWay.Random, AllAtOnce: true);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// What a play takes into your hand from your piles (HandTakeOf): each pile's cards it can take, by profile (DrawProfiles), as a
    /// TakeToHandAction. A take from every pile (Summon Forth) takes all it can from each. None with no pile read.
    /// </summary>
    private static IEnumerable<SimAction> TakesToHand(PlayFacts facts, Player me)
    {
        if (facts.TakenToHand <= 0 || HandTakeOf(facts.Card) is not { } take) yield break;
        foreach (var pile in take.Piles)
        {
            if (pile is not (PileType.Draw or PileType.Discard) || Eligible(me, pile == PileType.Discard, take.Eligible) is not { Count: > 0 } counts) continue;
            var fromDiscard = pile == PileType.Discard;
            if (take.Piles.Count > 1)
                yield return new FirstPlayOnly(new SimAction[] { new TakeToHandAction(counts.Values.Sum(), counts) { Random = take.Random, FromDiscard = fromDiscard } });
            else if (take.AllAtOnce)
                yield return new FirstPlayOnly(new SimAction[] { new TakeToHandAction(facts.TakenToHand, counts) { Random = take.Random, FromDiscard = fromDiscard } });
            else
                yield return new TakeToHandAction(Count: 1, counts) { Random = take.Random, FromDiscard = fromDiscard };
        }
    }

    /// <summary>How many cards of each profile in your draw or discard pile pass the filter.</summary>
    private static Dictionary<string, int> Eligible(Player me, bool fromDiscard, Func<CardModel, bool> eligible)
    {
        var counts = new Dictionary<string, int>();
        foreach (var other in (fromDiscard ? PileType.Discard : PileType.Draw).GetPile(me).Cards)
            if (eligible(other) && DrawProfiles.TryGetValue(other, out var profile)) counts[profile.Key] = counts.GetValueOrDefault(profile.Key) + 1;
        return counts;
    }

    /// <summary>
    /// What each play does to orbs, in order: slots it adds or removes, orbs it evokes, orbs it channels (into the targeted
    /// teammate's for Ignition; none when there's no teammate to aim at), then the Focus it gives or takes. Channelled orbs get
    /// their values when they're channelled. Storm and Trash to Treasure channel from their models.
    /// </summary>
    private static IEnumerable<SimAction> OrbsOf(CardModel card, Player me, ICombatState combat, string name, int channelTo)
    {
        var actions = new List<SimAction>();
        // What it does to your orbs, read from its program (PlayFacts.OrbsOf): slots, evokes, then each channel in its code's order.
        var plan = PlayFacts.OrbsOf(card);
        if (plan.Slots != 0) actions.Add(new OrbAction(OrbAction.Op.Slots) { Slots = plan.Slots });
        if (plan.EvokesEachOrb == 2) actions.Add(new OrbAction(OrbAction.Op.EvokeAllTwice));
        else if (plan.EvokesEachOrb > 0) actions.Add(new NotModelledAction($"every orb evoked {plan.EvokesEachOrb} times"));
        else if (plan.EvokesFront > 0) actions.Add(new OrbAction(OrbAction.Op.EvokeFront) { Times = plan.EvokesFront });
        // Aimed at no teammate (Ignition with none to aim at): nothing channelled.
        foreach (var (kind, times) in plan.Channels)
            if (channelTo != Who.None) actions.Add(new OrbAction(OrbAction.Op.Channel) { Orb = kind, Times = times, To = channelTo });
        var focus = FocusChange(card);
        if (focus != 0) actions.Add(new FocusAction(focus));
        return actions;
    }

    private static decimal SafeDecimal(Func<decimal> read)
    {
        try { return read(); } catch { return 0m; }
    }
}

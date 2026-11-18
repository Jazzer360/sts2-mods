namespace SpireQoL.Sim;

/// <summary>What happens around a card play: the card leaving your hand, and damage given as final numbers (the test shorthand's procs).</summary>
public static partial class SimEngine
{
    /// <summary>Damage given as final numbers: unpowered, from you, no card; to one enemy, every enemy, or a random one (a branch per target).</summary>
    private static void RunFixedHits(BranchSet b, FixedHitsAction fh)
    {
        int Dmg(SimEnemy e) => fh.Damage.TryGetValue(e.Id, out var d) ? d : 0;
        for (var t = 0; t < fh.Times; t++)
        {
            switch (fh.Spread)
            {
                case SimPlay.Target.Single:
                    b.Do(s => { if (s.Enemy(fh.TargetId) is { } e) Damage(s, e, Dmg(e)); });
                    break;
                case SimPlay.Target.All:
                    b.Do(s => HitAll(s, Dmg));
                    break;
                case SimPlay.Target.Random:
                    b.Each(s => HitRandom(s, Dmg));
                    break;
            }
        }
        b.Do(AllyDeaths);
    }

    /// <summary>
    /// The card has left its owner's hand (yours, or a teammate's for their auto-play): their hand's size; the hand Stampede and
    /// Simpleton pick from (cards it draws or makes aren't known); I Am Invincible stops being certain once its pile is drawn
    /// through; Fading; attacks this turn; Pael's Eye disarmed by a card played (not an auto-play).
    /// </summary>
    private static void CardResolved(SimState s, CardResolvedAction cr)
    {
        var k = cr.Owner == Who.You ? -1 : Who.TeammateIndex(cr.Owner);
        if (k >= s.Teammates.Count) { AllyDeaths(s); return; }
        var p = k < 0 ? s.P : s.MutableTeammate(k);
        // With a draw pile (decision #90), the cards it drew are already in hand, as drawn: only what it made or brought back is
        // unknown.
        var handDelta = cr.HandDelta - (p.Draw != null ? cr.Draws : 0);
        // A card added to a full hand goes to the discard pile instead (CardPileCmd.Add): never more than 10 (decision #90).
        p.HandCount = Math.Clamp(p.HandCount + handDelta, 0, p.Draw != null ? MaxHand : int.MaxValue);
        if (!cr.AutoPlay) p.PaelsEyeExhaust = false;
        if (p.AutoHand != null)
        {
            if (cr.HandCard != 0) p.AutoHand = p.AutoHand.Where(c => c.Id != cr.HandCard).ToList();
            if (handDelta > -1) s.AutoPlaysUnknown = true;
        }
        if (cr.Draws > 0 && p.InvincibleBlock > 0)
        {
            // Drawing the whole pile shuffles the discard pile in: the top isn't known any more.
            p.InvincibleDrawPile -= cr.Draws;
            if (p.InvincibleDrawPile <= 0) p.InvincibleBlock = 0;
        }
        p.Fading = Math.Max(0, p.Fading + cr.FadingDelta);
        p.HexFading = Math.Max(0, p.HexFading + cr.HexFadingDelta);
        if (cr.IsAttack) p.AttacksThisTurn++;
        AllyDeaths(s);
    }
}

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
    /// The card has left your hand: your hand's size; the hand Stampede and Simpleton pick from (cards it draws or makes aren't
    /// known); I Am Invincible stops being certain once its pile is drawn through; Fading; attacks this turn; Pael's Eye disarmed by
    /// a card you play (not an auto-play).
    /// </summary>
    private static void CardResolved(SimState s, CardResolvedAction cr)
    {
        s.P.HandCount = Math.Max(0, s.P.HandCount + cr.HandDelta);
        if (!cr.AutoPlay) s.P.PaelsEyeExhaust = false;
        if (s.P.AutoHand != null)
        {
            if (cr.HandCard != 0) s.P.AutoHand = s.P.AutoHand.Where(c => c.Id != cr.HandCard).ToList();
            if (cr.HandDelta > -1) s.AutoPlaysUnknown = true;
        }
        if (cr.Draws > 0 && s.P.InvincibleBlock > 0)
        {
            // Drawing the whole pile shuffles your discard pile in: the top isn't known any more.
            s.P.InvincibleDrawPile -= cr.Draws;
            if (s.P.InvincibleDrawPile <= 0) s.P.InvincibleBlock = 0;
        }
        s.P.Fading = Math.Max(0, s.P.Fading + cr.FadingDelta);
        if (cr.IsAttack) s.P.AttacksThisTurn++;
        AllyDeaths(s);
    }
}

namespace SpireQoL.Sim;

/// <summary>What happens around a card play: the card leaving your hand, and damage given as final numbers (the test shorthand's procs).</summary>
public static partial class SimEngine
{
    /// <summary>Damage given as final numbers: unpowered, from you, no card; to one enemy, every enemy, or a random one (a branch per target).</summary>
    private static void RunFixedHits(BranchSet branches, FixedHitsAction fixedHitsAction)
    {
        int Dmg(SimEnemy enemy) => fixedHitsAction.Damage.TryGetValue(enemy.Id, out var damage) ? damage : 0;
        for (var time = 0; time < fixedHitsAction.Times; time++)
        {
            switch (fixedHitsAction.Spread)
            {
                case SimPlay.Target.Single:
                    branches.Do(branch => { if (branch.Enemy(fixedHitsAction.TargetId) is { } enemy) Damage(branch, enemy, Dmg(enemy)); });
                    break;
                case SimPlay.Target.All:
                    branches.Do(branch => HitAll(branch, Dmg));
                    break;
                case SimPlay.Target.Random:
                    branches.Each(branch => HitRandom(branch, Dmg));
                    break;
            }
        }
        branches.Do(AllyDeaths);
    }

    /// <summary>
    /// The card has left its owner's hand (yours, or a teammate's for their auto-play): their hand's size; the hand Stampede and
    /// Simpleton pick from (cards it draws or makes aren't known); I Am Invincible stops being certain once its pile is drawn
    /// through; Fading; attacks this turn; Pael's Eye disarmed by a card played (not an auto-play).
    /// </summary>
    private static void CardResolved(SimState state, CardResolvedAction cardResolvedAction)
    {
        if (state.Player(cardResolvedAction.Owner) == null) { AllyDeaths(state); return; }
        var player = state.MutablePlayer(cardResolvedAction.Owner);
        if (cardResolvedAction.HandCard != 0) player.PlayingFromHand = Math.Max(0, player.PlayingFromHand - 1); // it resolved: HandCount drops it now
        // With a draw pile (decision #90), the cards it drew are already in hand, as drawn: only what it made or brought back is
        // unknown.
        var handDelta = cardResolvedAction.HandDelta - (player.Draw != null ? cardResolvedAction.Draws : 0);
        // A card added to a full hand goes to the discard pile instead (CardPileCmd.Add): never more than 10 (decision #90).
        // The cap is on the hand: the cards still being played (a play inside this one's: Hellraiser) are counted, not held.
        player.HandCount = Math.Clamp(player.HandCount + handDelta, 0, player.Draw != null ? MaxHand + player.PlayingFromHand : int.MaxValue);
        if (!cardResolvedAction.AutoPlay) player.PaelsEyeExhaust = false;
        if (player.AutoHand != null)
        {
            if (cardResolvedAction.HandCard != 0) player.AutoHand = player.AutoHand.Where(handCard => handCard.Id != cardResolvedAction.HandCard).ToList();
            if (handDelta > -1) state.AutoPlaysUnknown = true;
        }
        if (cardResolvedAction.Draws > 0 && player.InvincibleBlock > 0)
        {
            // Drawing the whole pile shuffles the discard pile in: the top isn't known any more.
            player.InvincibleDrawPile -= cardResolvedAction.Draws;
            if (player.InvincibleDrawPile <= 0) player.InvincibleBlock = 0;
        }
        player.Fading = Math.Max(0, player.Fading + cardResolvedAction.FadingDelta);
        player.HexFading = Math.Max(0, player.HexFading + cardResolvedAction.HexFadingDelta);
        if (cardResolvedAction.IsAttack) player.AttacksThisTurn++;
        AllyDeaths(state);
    }
}

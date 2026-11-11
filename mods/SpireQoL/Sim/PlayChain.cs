namespace SpireQoL.Sim;

/// <summary>
/// What a card play sets off in chains, on plain numbers (no game types, so it's tested offline):
/// cards exhausted → draws (Dark Embrace, Joss Paper), a Power played → draws (Game Piece), the
/// play count → draws (Iron Club), Vulnerable applied → draws (Vicious), HP lost → draws (Centennial
/// Puzzle); then the hand limit, draw blockers, and whether the draws shuffle the discard pile in.
/// PlayFacts fills the inputs from the game; the Shuffle Warning, draw procs and the audit read the
/// result. Decompile v0.111.0: the sources' hooks, CardPileCmd.DrawInternal and ShuffleIfNecessary.
/// </summary>
public static class PlayChain
{
    public const int MaxHand = 10; // CardPile.MaxCardsInHand

    public sealed class DrawInputs
    {
        /// <summary>Cards the card itself draws, across plays.</summary>
        public int OwnDraws;
        public int Plays = 1;
        public bool IsPower;
        /// <summary>Cards the play exhausts (itself and others; none caused by Ethereal).</summary>
        public int Exhausts;
        /// <summary>Of those, cards exhausted from your hand before it draws (they make room).</summary>
        public int ExhaustedFromHand;
        /// <summary>Cards it exhausts from the draw pile (Cleanse): the pile is smaller when it draws.</summary>
        public int ExhaustedFromDrawPile;
        /// <summary>Cards in hand before the play, this card included.</summary>
        public int HandCount;
        public int DrawPile;
        public int DiscardPile;
        /// <summary>Cards it discards from hand before drawing (Calculated Gamble): they're in the discard pile when it draws.</summary>
        public int DiscardedBeforeDraw;
        /// <summary>Vulnerable applications that land on enemies (Vicious draws per application).</summary>
        public int VulnerableLandings;
        public bool LosesHp;

        // Sources (0 / -1 = you don't have it).
        public int DarkEmbrace;
        public int Vicious;
        /// <summary>Joss Paper: cards exhausted toward its count, and the count (-1 = no relic).</summary>
        public int JossPaperCount = -1;
        public int JossPaperEvery = 5;
        /// <summary>Game Piece: cards drawn per Power played (0 = no relic).</summary>
        public int GamePiece;
        /// <summary>Iron Club: cards played toward its count, and the count (-1 = no relic).</summary>
        public int IronClubCount = -1;
        public int IronClubEvery = 4;
        /// <summary>Centennial Puzzle: cards it draws on your first HP loss this combat (0 = none or used).</summary>
        public int CentennialPuzzle;
        /// <summary>No Draw or Fiddle: no draws during your turn.</summary>
        public bool Blocked;
        /// <summary>
        /// It puts your hand into the draw pile and shuffles your discard pile in before drawing (Reboot:
        /// CardPileCmd.Shuffle in its play, whatever the piles hold).
        /// </summary>
        public bool ShufflesFirst;
        /// <summary>
        /// Cards your powers add to your hand after the play (CalamityPower.AfterCardPlayed): they run
        /// before your relics' draws (Iron Club, Game Piece) and take hand room first.
        /// </summary>
        public int AddedToHandAfterPlay;
    }

    public readonly record struct DrawResult(int Draws, int Chained, bool Shuffles);

    public static DrawResult Resolve(DrawInputs i)
    {
        // During the play: its own draws and what reacts to its exhausts, debuffs and HP loss.
        var during = i.DarkEmbrace * i.Exhausts
                     + i.Vicious * i.VulnerableLandings;
        if (i.JossPaperCount >= 0 && i.JossPaperEvery > 0 && i.Exhausts > 0)
            during += (i.JossPaperCount % i.JossPaperEvery + i.Exhausts) / i.JossPaperEvery;
        if (i.LosesHp) during += i.CentennialPuzzle;
        // After it (relics' AfterCardPlayed): they run after your powers', so cards your powers add to
        // your hand then (Calamity) take room first.
        var after = 0;
        if (i.IsPower) after += i.GamePiece * Math.Max(1, i.Plays);
        if (i.IronClubCount >= 0 && i.IronClubEvery > 0)
            after += (i.IronClubCount % i.IronClubEvery + Math.Max(1, i.Plays)) / i.IronClubEvery;
        var chained = during + after;

        int room, cards, pile;
        if (i.ShufflesFirst)
        {
            // Your hand is in the draw pile with everything else: a whole hand of room.
            pile = Math.Max(0, i.DrawPile - i.ExhaustedFromDrawPile) + i.DiscardPile + Math.Max(0, i.HandCount - 1 - i.ExhaustedFromHand);
            cards = pile;
            room = MaxHand;
        }
        else
        {
            // The card has left your hand; cards it exhausts from hand first make room.
            room = Math.Max(0, MaxHand - (i.HandCount - 1 - i.ExhaustedFromHand - i.DiscardedBeforeDraw));
            pile = Math.Max(0, i.DrawPile - i.ExhaustedFromDrawPile);
            // Drawing stops when both piles are empty (CardPileCmd.Draw).
            cards = pile + i.DiscardPile + i.DiscardedBeforeDraw;
        }
        if (i.Blocked) return new DrawResult(0, chained, i.ShufflesFirst);
        var first = Math.Min(Math.Min(i.OwnDraws + during, room), cards);
        var lateRoom = Math.Max(0, room - first - i.AddedToHandAfterPlay);
        var draws = first + Math.Min(Math.Min(after, lateRoom), cards - first);
        var shuffles = i.ShufflesFirst || (draws > pile && i.DiscardPile + i.DiscardedBeforeDraw > 0);
        return new DrawResult(draws, chained, shuffles);
    }
}

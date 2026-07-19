namespace Lucky5.Domain.Game.CleanRoom;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Group info for hand evaluation with wilds.
/// </summary>
internal sealed class GroupInfo
{
    public int Rank { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Wild Witch cabinet variant (Video Klein 6502 lineage).
/// Features:
/// - 53-card deck with Joker/Wild card
/// - Wild card substitutes for any rank/suit
/// - Double-up: BIG/SMALL next-card vs fixed threshold 7
/// - Ace counts HI or LO (auto-win both ways in double-up)
/// - 5♠ never loses when buying (SafeFail)
/// - Progressive jackpots: 4K-A (Aces), 4K-B (2s-4s), SF, RF
/// - Operator settings: max bet 100, percentage modes 85/30/40/50%
/// </summary>
public sealed class WildWitchCabinetVariant : ICabinetVariantEngine
{
    public string GameId => "wildwitch";

    public IReadOnlyList<CleanRoomCard> BuildDeck() => BuildWildWitchDeck().ToList();

    private static readonly CleanRoomCard JokerCard = new(15, 'J'); // Special Joker/Wild rank
    private static readonly CleanRoomCard FiveOfSpades = new(5, 'S');
    private const int DoubleUpThreshold = 7; // BIG >= 7, SMALL < 7 (but Ace auto-wins both ways)

    public IDoubleUpSession StartDoubleUp(int openingAmount, ulong seedRoot, int machineCreditBaseline, int boardBetAmount)
    {
        // Wild Witch double-up uses BIG/SMALL against fixed threshold
        var options = new Lucky5DoubleUpOptions
        {
            MaxSwitchesPerRound = 1, // Single switch in Wild Witch
            FirstLuckyMultiplier = 2,
            RepeatLuckyMultiplier = 2,
            MaxCreditLimit = 10_000_000,
            AceCountsHiOrLo = true,
            LuckyFiveArmsNoLose = true
        };

        var deck = BuildWildWitchDeck().ToArray();
        var rng = new SplitMix64Rng(DeterministicSeed.Derive(seedRoot, "wildwitch-doubleup"));
        rng.Shuffle(deck);

        return new Lucky5DoubleUpSession(
            SeedRoot: seedRoot,
            RoundSeedToken: DeterministicSeed.Derive(seedRoot, "wildwitch-doubleup-round", 0),
            Deck: deck,
            DealerIndex: 0,
            DealerCard: deck[0],
            CurrentAmount: openingAmount,
            MachineCreditBaseline: machineCreditBaseline,
            CurrentRoundIndex: 0,
            SwitchCountInRound: 0,
            LuckyHitCount: 0,
            IsNoLoseActive: false,
            Options: options,
            PlayedDealerIndexes: [],
            CurrentBoardCards: [deck[0]],
            CurrentBoardComplete: false,
            BoardHandRank: null,
            LastBoardBonusAmount: 0,
            BoardBonusTotal: 0,
            LastResolvedBoardSlotIndex: 1,
            BetAmount: boardBetAmount);
    }

    public HandEvaluation EvaluateHand(IReadOnlyList<CleanRoomCard> handCards)
    {
        if (handCards.Count != 5)
        {
            throw new ArgumentException("Exactly five cards are required.", nameof(handCards));
        }

        // Count jokers/wild cards
        var wildCount = handCards.Count(c => c.Rank == 15);
        var naturalCards = handCards.Where(c => c.Rank != 15).ToArray();

        if (wildCount == 0)
        {
            return FiveCardDrawEngine.EvaluateHand(handCards);
        }

        // With wild cards, find the best possible hand
        return EvaluateWithWilds(naturalCards, wildCount);
    }

    public bool MeetsVariantSpecificProgressiveCondition(IReadOnlyList<CleanRoomCard> resultCards, string serializedVariantState)
    {
        // Wild Witch progressive conditions:
        // 1. 4K-A: Four Aces (with or without wild)
        // 2. 4K-B: Four 2s, 3s, or 4s
        // 3. SF: Straight Flush (natural or with wild)
        // 4. RF: Royal Flush (natural or with wild)

        if (resultCards.Count != 5) return false;

        var eval = EvaluateHand(resultCards);

        return eval.Category switch
        {
            HandCategory.FourOfAKind when eval.Tiebreak[0] == 14 => true, // 4K-A
            HandCategory.FourOfAKind when eval.Tiebreak[0] >= 2 && eval.Tiebreak[0] <= 4 => true, // 4K-B
            HandCategory.StraightFlush => true,
            HandCategory.RoyalFlush => true,
            _ => false
        };
    }

    private static HandEvaluation EvaluateWithWilds(CleanRoomCard[] naturalCards, int wildCount)
    {
        if (naturalCards.Length == 0)
        {
            // All wilds = Five of a Kind (highest possible)
            return new HandEvaluation(HandCategory.FiveOfAKind, "Five of a Kind (Wild)", [15]);
        }

        var ranks = naturalCards.Select(c => c.Rank).OrderByDescending(r => r).ToArray();
        var suits = naturalCards.Select(c => c.Suit).ToArray();
        var isFlush = suits.All(s => s == suits[0]);

        var groups = naturalCards
            .GroupBy(c => c.Rank)
            .Select(g => new GroupInfo { Rank = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Rank)
            .ToList();

        // Try to make the best hand with available wilds
        // var bestCategory = HandCategory.HighCard; // Unused - removed
        // var bestTiebreak = ranks; // Unused - removed
        // var bestDisplay = "High Card"; // Unused - removed

        // Check Five of a Kind (needs 4 natural + 1 wild, or 3+2, etc.)
        if (groups.Count > 0 && groups[0].Count + wildCount >= 5)
        {
            return new HandEvaluation(HandCategory.FiveOfAKind, "Five of a Kind", [groups[0].Rank]);
        }

        // Check Straight Flush / Royal Flush
        var straightResult = CheckStraightWithWilds(ranks, wildCount);
        if (straightResult.IsStraight && isFlush)
        {
            if (straightResult.HighCard == 14)
            {
                return new HandEvaluation(HandCategory.RoyalFlush, "Royal Flush (Wild)", [14]);
            }
            return new HandEvaluation(HandCategory.StraightFlush, "Straight Flush (Wild)", [straightResult.HighCard]);
        }

        // Check Four of a Kind
        if (groups.Count > 0 && groups[0].Count + wildCount >= 4)
        {
            var quadRank = groups[0].Rank;
            var kicker = groups.Count > 1 ? groups[1].Rank : (wildCount > groups[0].Count ? 15 : 0);
            return new HandEvaluation(HandCategory.FourOfAKind, "Four of a Kind (Wild)", [quadRank, kicker]);
        }

        // Check Full House
        if (CanMakeFullHouse(groups, wildCount))
        {
            var tripRank = groups[0].Rank;
            var pairRank = groups.Count > 1 ? groups[1].Rank : tripRank;
            return new HandEvaluation(HandCategory.FullHouse, "Full House (Wild)", [tripRank, pairRank]);
        }

        // Check Flush
        if (isFlush && wildCount >= 0) // Natural flush or can complete with wilds
        {
            return new HandEvaluation(HandCategory.Flush, "Flush (Wild)", ranks);
        }

        // Check Straight
        if (straightResult.IsStraight)
        {
            return new HandEvaluation(HandCategory.Straight, "Straight (Wild)", [straightResult.HighCard]);
        }

        // Check Three of a Kind
        if (groups.Count > 0 && groups[0].Count + wildCount >= 3)
        {
            var tripRank = groups[0].Rank;
            var kickers = ranks.Where(r => r != tripRank).OrderByDescending(r => r).ToArray();
            return new HandEvaluation(HandCategory.ThreeOfAKind, "Three of a Kind (Wild)", [tripRank, ..kickers]);
        }

        // Check Two Pair
        var pairCount = groups.Count(g => g.Count >= 2);
        if (pairCount + wildCount >= 2)
        {
            var pairRanks = groups.Where(g => g.Count >= 2).Select(g => g.Rank).OrderByDescending(r => r).ToArray();
            var kicker = ranks.FirstOrDefault(r => !pairRanks.Contains(r));
            return new HandEvaluation(HandCategory.TwoPair, "Two Pair (Wild)", [..pairRanks, kicker]);
        }

        // Check One Pair (Jacks or Better for payout)
        if (groups.Count > 0 && (groups[0].Count + wildCount >= 2))
        {
            var pairRank = groups[0].Rank;
            var kickers = ranks.Where(r => r != pairRank).OrderByDescending(r => r).ToArray();
            return new HandEvaluation(HandCategory.OnePair, "One Pair (Wild)", [pairRank, ..kickers], pairRank);
        }

        return new HandEvaluation(HandCategory.HighCard, "High Card", ranks);
    }

    private static bool CanMakeFullHouse(IReadOnlyList<GroupInfo> groups, int wildCount)
        {
            // Need at least 2 natural cards of one rank and 2 of another, or 3+2 with wilds filling gaps
            var pairs = groups.Count(g => g.Count >= 2);
            var trips = groups.Count(g => g.Count >= 3);

            if (trips >= 1 && pairs >= 2) return true; // Natural full house
            if (trips >= 1 && pairs >= 1 && wildCount >= 1) return true; // Trip + pair + wild for second pair
            if (pairs >= 2 && wildCount >= 1) return true; // Two pairs + wild for trips
            if (trips >= 1 && wildCount >= 2) return true; // Trips + 2 wilds for pair

            return false;
        }

    private static (bool IsStraight, int HighCard) CheckStraightWithWilds(int[] ranks, int wildCount)
    {
        var uniqueRanks = ranks.Distinct().OrderBy(r => r).ToArray();

        // Check normal straights
        for (int high = 14; high >= 5; high--)
        {
            var needed = 0;
            for (int r = high; r >= high - 4; r--)
            {
                if (!uniqueRanks.Contains(r)) needed++;
            }
            if (needed <= wildCount)
            {
                return (true, high);
            }
        }

        // Check wheel (A-2-3-4-5)
        var wheelNeeded = 0;
        foreach (var r in new[] { 14, 2, 3, 4, 5 })
        {
            if (!uniqueRanks.Contains(r)) wheelNeeded++;
        }
        if (wheelNeeded <= wildCount)
        {
            return (true, 5);
        }

        return (false, 0);
    }

    private static IEnumerable<CleanRoomCard> BuildWildWitchDeck()
    {
        // Standard 52 cards
        foreach (var suit in new[] { 'C', 'D', 'H', 'S' })
        {
            for (var rank = 2; rank <= 14; rank++)
            {
                yield return new CleanRoomCard(rank, suit);
            }
        }

        // Add Joker/Wild card (rank 15, suit J for Joker)
        yield return new CleanRoomCard(15, 'J');
    }
}
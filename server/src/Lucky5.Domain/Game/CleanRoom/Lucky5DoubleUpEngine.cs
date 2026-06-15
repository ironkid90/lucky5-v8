namespace Lucky5.Domain.Game.CleanRoom;

public static class Lucky5DoubleUpEngine
{
    public static Lucky5DoubleUpSession Start(
        int openingAmount,
        ulong seedRoot,
        int machineCreditBaseline = 0,
        Lucky5DoubleUpOptions? options = null)
    {
        var resolvedOptions = options ?? new Lucky5DoubleUpOptions(MaxCreditLimit: Decimal.ToInt32(EngineConfig.Default.CloseThreshold));
        var deck = FiveCardDrawEngine.ShuffleDeck(seedRoot, "double-up");
        return CreateSessionFromDeck(seedRoot, deck, openingAmount, machineCreditBaseline, resolvedOptions);
    }

    public static Lucky5DoubleUpSession CreateSessionFromDeck(
        ulong seedRoot,
        IEnumerable<CleanRoomCard> deck,
        int openingAmount,
        int machineCreditBaseline = 0,
        Lucky5DoubleUpOptions? options = null)
    {
        if (openingAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openingAmount), openingAmount, "Opening amount must be positive.");
        }

        var resolvedOptions = options ?? new Lucky5DoubleUpOptions(MaxCreditLimit: Decimal.ToInt32(EngineConfig.Default.CloseThreshold));
        var deckArray = deck.ToArray();
        if (deckArray.Length < 2)
        {
            throw new ArgumentException("Double-up deck must contain at least two cards.", nameof(deck));
        }

        var roundSeed = DeterministicSeed.Derive(seedRoot, "double-up-round", 0);
        var session = new Lucky5DoubleUpSession(
            SeedRoot: seedRoot,
            RoundSeedToken: roundSeed,
            Deck: deckArray,
            DealerIndex: 0,
            DealerCard: deckArray[0],
            CurrentAmount: openingAmount,
            MachineCreditBaseline: machineCreditBaseline,
            CurrentRoundIndex: 0,
            SwitchCountInRound: 0,
            LuckyHitCount: 0,
            // Lucky 5 no-lose mode only arms when 5S is found via an explicit SWITCH.
            // The opening dealer card and a high/low challenger reveal must not activate it.
            IsNoLoseActive: false,
            Options: resolvedOptions,
            PlayedDealerIndexes: Array.Empty<int>());

        return session;
    }

    public static Lucky5DoubleUpSession SwitchDealer(Lucky5DoubleUpSession session)
    {
        EnsurePlayable(session);

        if (session.SwitchCountInRound >= session.Options.MaxSwitchesPerRound)
        {
            throw new InvalidOperationException("Maximum switch count reached for this double-up round.");
        }

        var nextDealerIndex = session.DealerIndex + 1;
        if (nextDealerIndex >= session.Deck.Length - 1)
        {
            throw new InvalidOperationException("No more cards available to switch the dealer.");
        }

        var dealerCard = session.Deck[nextDealerIndex];
        var isLuckyFive = dealerCard.Rank == 5 && dealerCard.Suit == 'S';
        var luckyHitCount = isLuckyFive ? session.LuckyHitCount + 1 : session.LuckyHitCount;
        var multiplier = isLuckyFive
            ? session.LuckyHitCount == 0
                ? session.Options.FirstLuckyMultiplier
                : session.Options.RepeatLuckyMultiplier
            : 1;

        var switchedSession = session with
        {
            DealerIndex = nextDealerIndex,
            DealerCard = dealerCard,
            CurrentAmount = checked(session.CurrentAmount * multiplier),
            SwitchCountInRound = session.SwitchCountInRound + 1,
            LuckyHitCount = luckyHitCount,
            IsNoLoseActive = session.IsNoLoseActive || isLuckyFive
        };

        return switchedSession;
    }

    public static Lucky5DoubleUpSession SwapChallenger(Lucky5DoubleUpSession session, int swapPosition)
    {
        EnsurePlayable(session);

        if (swapPosition < 0 || swapPosition >= session.Deck.Length)
        {
            throw new InvalidOperationException("Invalid swap position.");
        }

        if (swapPosition <= session.DealerIndex)
        {
            throw new InvalidOperationException("Cannot swap to a card at or before the dealer position.");
        }

        var challengerCard = session.Deck[swapPosition];
        var swappedSession = session with
        {
            SwapActivePosition = swapPosition
        };

        return swappedSession;
    }

    public static Lucky5DoubleUpResolution ResolveGuess(Lucky5DoubleUpSession session, BigSmallGuess guess)
    {
        EnsurePlayable(session);

        var challengerIndex = session.SwapActivePosition >= 0
            ? session.SwapActivePosition
            : session.DealerIndex + 1;
        if (challengerIndex >= session.Deck.Length)
        {
            throw new InvalidOperationException("No challenger card available for double-up resolution.");
        }

        var challengerCard = session.Deck[challengerIndex];
        var previousAmount = session.CurrentAmount;
        var playerWins = IsWinningGuess(session.DealerCard, challengerCard, guess, session.Options);

        if (playerWins)
        {
            var nextAmount = checked(previousAmount * 2);
            var continuedSession = session with
            {
                DealerIndex = challengerIndex,
                DealerCard = challengerCard,
                CurrentAmount = nextAmount,
                CurrentRoundIndex = session.CurrentRoundIndex + 1,
                SwitchCountInRound = 0,
                LuckyHitCount = 0,
                IsNoLoseActive = session.IsNoLoseActive,
                SwapActivePosition = -1,
                PlayedDealerIndexes = AppendPlayedDealerIndex(session)
            };

            var shouldCloseMachine = session.MachineCreditBaseline < session.Options.MaxCreditLimit
                && session.MachineCreditBaseline + nextAmount >= session.Options.MaxCreditLimit;

            var resultSession = shouldCloseMachine
                ? continuedSession with
                {
                    IsTerminal = true,
                    TerminalOutcome = Lucky5DoubleUpOutcome.MachineClosed,
                    CashoutCredits = nextAmount
                }
                : continuedSession;

            var outcome = shouldCloseMachine
                ? Lucky5DoubleUpOutcome.MachineClosed
                : Lucky5DoubleUpOutcome.Win;

            return new Lucky5DoubleUpResolution(
                guess,
                session.DealerCard,
                challengerCard,
                outcome,
                previousAmount,
                resultSession.CurrentAmount,
                resultSession.CashoutCredits,
                resultSession);
        }

        if (session.IsNoLoseActive)
        {
            var safeSession = session with
            {
                DealerIndex = challengerIndex,
                DealerCard = challengerCard,
                CurrentRoundIndex = session.CurrentRoundIndex + 1,
                SwitchCountInRound = 0,
                LuckyHitCount = 0,
                IsNoLoseActive = false,
                IsTerminal = true,
                TerminalOutcome = Lucky5DoubleUpOutcome.SafeFail,
                CashoutCredits = previousAmount,
                PlayedDealerIndexes = AppendPlayedDealerIndex(session)
            };

            return new Lucky5DoubleUpResolution(
                guess,
                session.DealerCard,
                challengerCard,
                Lucky5DoubleUpOutcome.SafeFail,
                previousAmount,
                previousAmount,
                previousAmount,
                safeSession);
        }

        var loseSession = session with
        {
            DealerIndex = challengerIndex,
            DealerCard = challengerCard,
            CurrentAmount = 0,
            CurrentRoundIndex = session.CurrentRoundIndex + 1,
            SwitchCountInRound = 0,
            LuckyHitCount = 0,
            IsNoLoseActive = false,
            IsTerminal = true,
            TerminalOutcome = Lucky5DoubleUpOutcome.Lose,
            CashoutCredits = 0,
            PlayedDealerIndexes = AppendPlayedDealerIndex(session)
        };

        return new Lucky5DoubleUpResolution(
            guess,
            session.DealerCard,
            challengerCard,
            Lucky5DoubleUpOutcome.Lose,
            previousAmount,
            0,
            0,
            loseSession);
    }

    private static bool IsWinningGuess(
        CleanRoomCard dealerCard,
        CleanRoomCard challengerCard,
        BigSmallGuess guess,
        Lucky5DoubleUpOptions options)
    {
        if (options.AceCountsHiOrLo && challengerCard.Rank == 14)
        {
            return true;
        }

        if (options.AceCountsHiOrLo && dealerCard.Rank == 14)
        {
            return true;
        }

        return guess switch
        {
            BigSmallGuess.Big => challengerCard.Rank > dealerCard.Rank,
            BigSmallGuess.Small => challengerCard.Rank < dealerCard.Rank,
            _ => throw new ArgumentOutOfRangeException(nameof(guess), guess, "Unsupported double-up guess.")
        };
    }

    private static int[] AppendPlayedDealerIndex(Lucky5DoubleUpSession session)
    {
        var source = session.PlayedDealerIndexes ?? Array.Empty<int>();
        var result = new int[source.Length + 1];
        Array.Copy(source, result, source.Length);
        result[^1] = session.DealerIndex;
        return result;
    }

    private static void EnsurePlayable(Lucky5DoubleUpSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.IsTerminal)
        {
            throw new InvalidOperationException("Double-up session is already terminal.");
        }
    }
}

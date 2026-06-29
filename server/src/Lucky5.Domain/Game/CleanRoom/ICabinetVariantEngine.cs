namespace Lucky5.Domain.Game.CleanRoom;

using System.Collections.Generic;

/// <summary>
/// Defines the variant engine strategy pattern for different arcade games.
/// Implementation controls jackpot rules, paytable parsing, double-up resolution, and hand evaluation constraints.
/// </summary>
public interface ICabinetVariantEngine
{
    /// <summary>
    /// The canonical game identifier (e.g., "lucky5", "jacks-or-better", "deuces-wild").
    /// </summary>
    string GameId { get; }

    /// <summary>
    /// Starts a double-up session conforming to the specific variant's logic rules (e.g., Safe Fails, Joker replacements).
    /// </summary>
    IDoubleUpSession StartDoubleUp(int openingAmount, ulong seedRoot, int machineCreditBaseline, int boardBetAmount);

    /// <summary>
    /// Evaluates the hand array based strictly on this specific game engine's variance logic.
    /// </summary>
    HandEvaluation EvaluateHand(IReadOnlyList<CleanRoomCard> handCards);

    /// <summary>
    /// Evaluates a board condition against a variant-specific configuration (like the Lucky 5 'Kent').
    /// </summary>
    bool MeetsVariantSpecificProgressiveCondition(IReadOnlyList<CleanRoomCard> resultCards, string serializedVariantState);
}

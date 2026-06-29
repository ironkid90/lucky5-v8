namespace Lucky5.Domain.Game.CleanRoom;

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class Lucky5CabinetVariant : ICabinetVariantEngine
{
	public string GameId => "lucky5";

	public IDoubleUpSession StartDoubleUp(int openingAmount, ulong seedRoot, int machineCreditBaseline, int boardBetAmount)
	{
		return Lucky5DoubleUpEngine.Start(
			openingAmount: openingAmount,
			seedRoot: seedRoot,
			machineCreditBaseline: machineCreditBaseline,
			options: null,
			boardBetAmount: boardBetAmount
		);
	}

	public HandEvaluation EvaluateHand(IReadOnlyList<CleanRoomCard> handCards)
	{
		// Lucky 5 uses explicit video poker standard (Jacks or Better payouts apply usually to Two Pair minimum, Lebanese version specific config)
		return FiveCardDrawEngine.EvaluateHand(handCards);
	}

	public bool MeetsVariantSpecificProgressiveCondition(IReadOnlyList<CleanRoomCard> resultCards, string serializedVariantState)
	{
		// Lucky 5 uses the explicit "Kent" jackpot - a 5-card straight drawn in exact positional order.
		if (resultCards.Count != 5) return false;

		bool ascending = true;
		for (var i = 1; i < 5; i++)
		{
			if (resultCards[i].Rank != resultCards[i - 1].Rank + 1)
			{
				ascending = false;
				break;
			}
		}
		if (ascending) return true;

		bool descending = true;
		for (var i = 1; i < 5; i++)
		{
			if (resultCards[i].Rank != resultCards[i - 1].Rank - 1)
			{
				descending = false;
				break;
			}
		}
		return descending;
	}
}
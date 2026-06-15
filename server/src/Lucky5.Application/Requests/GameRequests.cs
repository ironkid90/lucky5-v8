namespace Lucky5.Application.Requests;

public sealed record StartDoubleUpRequest(Guid RoundId);
public sealed record SwitchDealerRequest(Guid RoundId);
public sealed record DoubleUpGuessRequest(Guid RoundId, string Guess);
public sealed record CabinetReconnectRequest(long LastStateVersion, long LastSequenceNumber);
public sealed record CashoutDoubleUpRequest(Guid RoundId);
public sealed record TakeHalfRequest(Guid RoundId);
public sealed record ChangeJackpotRankRequest(int MachineId, int Rank);

public sealed record MachineCashRequest(decimal Amount);

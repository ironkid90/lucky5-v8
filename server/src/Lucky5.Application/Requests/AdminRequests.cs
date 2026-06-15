namespace Lucky5.Application.Requests;

using Lucky5.Domain.Entities;

public sealed record AdminCreditRequest(
    Guid TargetUserId,
    decimal Amount,
    string Reason);

public sealed record SetDoorStateRequest(
    DoorState DoorState);

public sealed record RechargeBonusRequest(
    Guid UserId,
    decimal RechargeAmount);

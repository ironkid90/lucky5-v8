namespace Lucky5.Application.Dtos;

public sealed record AgentDto(
    int Id,
    string Name,
    string Code,
    string PhoneNumber,
    bool IsActive,
    decimal CreditPool,
    DateTime CreatedUtc);

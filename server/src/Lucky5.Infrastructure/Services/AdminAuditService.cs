namespace Lucky5.Infrastructure.Services;

using System.Threading;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;
using Lucky5.Domain.Entities;

public sealed class AdminAuditService(InMemoryDataStore store) : IAdminAuditService
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] SensitiveKeyFragments =
    [
        "secret",
        "token",
        "password",
        "credential",
        "apikey",
        "api_key",
        "authorization",
        "cookie"
    ];

    public Task<AdminAuditEntryDto> AppendAsync(AdminAuditWriteDto write, CancellationToken cancellationToken)
    {
        if (write.ActorUserId == Guid.Empty)
        {
            throw new InvalidOperationException("Audit actor is required");
        }

        if (string.IsNullOrWhiteSpace(write.Action))
        {
            throw new InvalidOperationException("Audit action is required");
        }

        if (string.IsNullOrWhiteSpace(write.TargetType))
        {
            throw new InvalidOperationException("Audit target type is required");
        }

        var sequence = Interlocked.Increment(ref store.AdminAuditSequence);
        var record = new AdminAuditRecord
        {
            SequenceNumber = sequence,
            CreatedUtc = DateTime.UtcNow,
            ActorUserId = write.ActorUserId,
            ActorRole = Normalize(write.ActorRole, "admin"),
            Action = Normalize(write.Action, "unknown"),
            TargetType = Normalize(write.TargetType, "unknown"),
            TargetId = Normalize(write.TargetId, string.Empty),
            MachineId = write.MachineId,
            CabinetDeviceId = write.CabinetDeviceId,
            Outcome = Normalize(write.Outcome, "succeeded"),
            Reason = RedactValue(write.Reason),
            Metadata = RedactMetadata(write.Metadata)
        };

        store.AdminAuditRecords.Enqueue(record);
        return Task.FromResult(ToDto(record));
    }

    public Task<IReadOnlyList<AdminAuditEntryDto>> ListRecentAsync(int take, CancellationToken cancellationToken)
    {
        var boundedTake = Math.Clamp(take, 1, 500);
        var records = store.AdminAuditRecords
            .OrderByDescending(record => record.SequenceNumber)
            .Take(boundedTake)
            .Select(ToDto)
            .ToArray();

        return Task.FromResult<IReadOnlyList<AdminAuditEntryDto>>(records);
    }

    private static AdminAuditEntryDto ToDto(AdminAuditRecord record)
        => new(
            record.Id,
            record.SequenceNumber,
            record.CreatedUtc,
            record.ActorUserId,
            record.ActorRole,
            record.Action,
            record.TargetType,
            record.TargetId,
            record.MachineId,
            record.CabinetDeviceId,
            record.Outcome,
            record.Reason,
            new Dictionary<string, string>(record.Metadata, KeyComparer));

    private static IReadOnlyDictionary<string, string> RedactMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return new Dictionary<string, string>(KeyComparer);
        }

        var safe = new Dictionary<string, string>(KeyComparer);
        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            safe[key.Trim()] = IsSensitiveKey(key) ? "<redacted>" : RedactValue(value);
        }

        return safe;
    }

    private static bool IsSensitiveKey(string key)
        => SensitiveKeyFragments.Any(fragment => key.Replace("-", string.Empty, StringComparison.Ordinal).Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string RedactValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length > 300 ? trimmed[..300] : trimmed;
    }

    private static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
    }
}
namespace Lucky5.Infrastructure.Services;

using System.Collections.Concurrent;
using Lucky5.Application.Contracts;
using Lucky5.Application.Dtos;

public sealed class InMemoryMachineStateCache(MachineCacheTtlOptions options) : IMachineStateCache
{
    private readonly record struct CacheEntry<T>(T Value, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, CacheEntry<ActiveRoundStateDto?>> _rounds = new();
    private readonly ConcurrentDictionary<string, CacheEntry<MachineSessionDto>> _sessions = new();

    private static string RoundKey(Guid userId, int machineId) => $"{userId:N}:{machineId}:round";
    private static string SessionKey(Guid userId, int machineId) => $"{userId:N}:{machineId}:session";

    public Task<ActiveRoundStateDto?> GetActiveRoundAsync(Guid userId, int machineId)
    {
        if (_rounds.TryGetValue(RoundKey(userId, machineId), out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
            return Task.FromResult(entry.Value);
        return Task.FromResult<ActiveRoundStateDto?>(null);
    }

    public void SetActiveRound(Guid userId, int machineId, ActiveRoundStateDto? dto)
    {
        var key = RoundKey(userId, machineId);
        var expiry = DateTimeOffset.UtcNow.Add(options.ActiveRoundTtl);
        _rounds[key] = new CacheEntry<ActiveRoundStateDto?>(dto, expiry);
    }

    public void InvalidateActiveRound(Guid userId, int machineId)
        => _rounds.TryRemove(RoundKey(userId, machineId), out _);

    public Task<MachineSessionDto?> GetMachineSessionAsync(Guid userId, int machineId)
    {
        if (_sessions.TryGetValue(SessionKey(userId, machineId), out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
            return Task.FromResult<MachineSessionDto?>(entry.Value);
        return Task.FromResult<MachineSessionDto?>(null);
    }

    public void SetMachineSession(Guid userId, int machineId, MachineSessionDto dto)
    {
        var key = SessionKey(userId, machineId);
        var expiry = DateTimeOffset.UtcNow.Add(options.MachineSessionTtl);
        _sessions[key] = new CacheEntry<MachineSessionDto>(dto, expiry);
    }

    public void InvalidateMachineSession(Guid userId, int machineId)
        => _sessions.TryRemove(SessionKey(userId, machineId), out _);
}

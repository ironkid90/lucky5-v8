using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lucky5.Domain.Entities;
using Lucky5.Application.Interfaces;
using Lucky5.Infrastructure.Services;

namespace Lucky5.Infrastructure.Data.Repositories;

/// <summary>
/// Adapter to make the existing InMemoryDataStore implement the new IDataStore interface,
/// allowing the application to run without a real database configured.
/// </summary>
public class InMemoryDataStoreAdapter : IDataStore
{
    private readonly InMemoryDataStore _store;

    public InMemoryDataStoreAdapter(InMemoryDataStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<User?> GetUserByUsernameAsync(string username)
    {
        var user = _store.Users.Values.FirstOrDefault(u => 
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user);
    }

    public Task<User?> GetUserByIdAsync(Guid userId)
    {
        _store.Users.TryGetValue(userId, out var user);
        return Task.FromResult(user);
    }

    public Task UpdateUserAsync(User user)
    {
        _store.Users[user.Id] = user;
        _store.Profiles[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task<MemberProfile?> GetProfileAsync(Guid userId)
    {
        _store.MemberProfiles.TryGetValue(userId, out var profile);
        return Task.FromResult(profile);
    }

    public Task UpdateProfileAsync(MemberProfile profile)
    {
        _store.MemberProfiles[profile.UserId] = profile;
        return Task.CompletedTask;
    }

    public Task<List<Machine>> GetMachinesAsync()
    {
        return Task.FromResult(_store.Machines.Values.ToList());
    }

    public Task<Machine?> GetMachineAsync(int machineId)
    {
        var machine = _store.Machines.Values.FirstOrDefault(m => m.Id == machineId);
        return Task.FromResult(machine);
    }

    public Task<List<Offer>> GetOffersAsync()
    {
        return Task.FromResult(_store.Offers.ToList());
    }

    public Task<MachineSessionState?> GetMachineSessionAsync(Guid userId, int machineId)
    {
        var session = _store.MachineSessions.Values
            .FirstOrDefault(s => s.UserId == userId && s.MachineId == machineId);
        return Task.FromResult(session);
    }

    public Task<MachineSessionState?> GetMachineSessionByIdAsync(Guid sessionId)
    {
        var session = _store.MachineSessions.Values
            .FirstOrDefault(s => s.SessionId == sessionId);
        return Task.FromResult(session);
    }

    public Task CreateMachineSessionAsync(MachineSessionState session)
    {
        _store.MachineSessions[session.SessionId] = session;
        _store.MachineSessionStates[$"{session.UserId:N}:{session.MachineId}"] = session;
        return Task.CompletedTask;
    }

    public Task UpdateMachineSessionAsync(MachineSessionState session)
    {
        lock (_store.LedgerSync)
        {
            session.LastUpdatedUtc = DateTime.UtcNow;
            _store.MachineSessions[session.SessionId] = session;
            _store.MachineSessionStates[$"{session.UserId:N}:{session.MachineId}"] = session;
        }

        return Task.CompletedTask;
    }

    public Task DeleteMachineSessionAsync(Guid sessionId)
    {
        lock (_store.LedgerSync)
        {
            if (_store.MachineSessions.TryRemove(sessionId, out var session))
            {
                _store.MachineSessionStates.TryRemove($"{session.UserId:N}:{session.MachineId}", out _);
            }
        }

        return Task.CompletedTask;
    }

    public Task<MachineLedgerState> GetOrInitializeMachineLedgerAsync(int machineId)
    {
        if (!_store.MachineLedgers.TryGetValue(machineId, out var ledger))
        {
            _store.Machines.TryGetValue(machineId, out var machine);
            ledger = new MachineLedgerState
            {
                MachineId = machineId,
                MachineSerial = machine?.MachineSerial ?? string.Empty,
                MachineSerie = machine?.MachineSerie ?? string.Empty,
                MachineKent = machine?.MachineKent ?? string.Empty,
                TargetRtp = Lucky5.Domain.Game.CleanRoom.EngineConfig.Default.TargetRtp,
                LastPayoutScale = Lucky5.Domain.Game.CleanRoom.EngineConfig.Default.DefaultPayoutScale
            };
            _store.MachineLedgers[machineId] = ledger;
        }
        return Task.FromResult(ledger);
    }

    public Task UpdateMachineLedgerAsync(MachineLedgerState ledger)
    {
        lock (_store.LedgerSync)
        {
            _store.MachineLedgers[ledger.MachineId] = ledger;
        }

        return Task.CompletedTask;
    }

    public Task<GameRound?> GetLatestRoundAsync(Guid userId, int machineId)
    {
        var round = _store.ActiveRounds.Values
            .Where(r => r.UserId == userId && r.MachineId == machineId)
            .OrderByDescending(r => r.CreatedUtc)
            .FirstOrDefault();
        return Task.FromResult(round);
    }

    public Task<GameRound?> GetRoundAsync(Guid roundId)
    {
        _store.ActiveRounds.TryGetValue(roundId, out var round);
        return Task.FromResult(round);
    }

    public Task SaveRoundAsync(GameRound round)
    {
        lock (_store.LedgerSync)
        {
            _store.ActiveRounds[round.RoundId] = round;
        }

        return Task.CompletedTask;
    }

    public Task AddWalletLedgerEntryAsync(WalletLedgerEntry entry)
    {
        lock (_store.LedgerSync)
        {
            _store.Ledger.Add(entry);
            _store.WalletLedger.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<CabinetCommandRecord?> GetCabinetCommandRecordAsync(Guid userId, Guid commandId, string idempotencyKey)
    {
        var commandKey = BuildCommandKey(userId, commandId, idempotencyKey);
        if (!_store.CabinetCommandRecords.TryGetValue(commandKey, out var record))
        {
            record = _store.CabinetCommandRecords.Values.FirstOrDefault(candidate =>
                candidate.UserId == userId
                && (candidate.CommandId == commandId || string.Equals(candidate.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)));
        }

        return Task.FromResult(record);
    }

    public Task SaveCabinetCommandRecordAsync(CabinetCommandRecord record)
    {
        lock (_store.LedgerSync)
        {
            _store.CabinetCommandRecords[BuildCommandKey(record.UserId, record.CommandId, record.IdempotencyKey)] = record;
        }

        return Task.CompletedTask;
    }

    public Task<CabinetStateCursor> GetOrInitializeCabinetStateCursorAsync(Guid userId, int machineId)
    {
        var cursorKey = BuildCursorKey(userId, machineId);
        var cursor = _store.CabinetStateCursors.GetOrAdd(cursorKey, _ => new CabinetStateCursor
        {
            UserId = userId,
            MachineId = machineId
        });

        return Task.FromResult(cursor);
    }

    public Task<CabinetStateCursor> AdvanceCabinetStateCursorAsync(Guid userId, int machineId)
    {
        lock (_store.LedgerSync)
        {
            var cursorKey = BuildCursorKey(userId, machineId);
            if (!_store.CabinetStateCursors.TryGetValue(cursorKey, out var cursor))
            {
                cursor = new CabinetStateCursor
                {
                    UserId = userId,
                    MachineId = machineId
                };
                _store.CabinetStateCursors[cursorKey] = cursor;
            }

            cursor.StateVersion++;
            cursor.SequenceNumber++;
            cursor.UpdatedUtc = DateTime.UtcNow;

            return Task.FromResult(cursor);
        }
    }

    public Task SaveCabinetEventRecordAsync(CabinetEventRecord record)
    {
        lock (_store.LedgerSync)
        {
            _store.CabinetEventRecords.Enqueue(record);

            while (_store.CabinetEventRecords.Count > 512 && _store.CabinetEventRecords.TryDequeue(out _))
            {
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CabinetEventRecord>> GetCabinetEventRecordsAfterAsync(Guid userId, int machineId, long sequenceNumber, int maxCount)
    {
        var records = _store.CabinetEventRecords
            .Where(record => record.UserId == userId
                && record.MachineId == machineId
                && record.SequenceNumber > sequenceNumber)
            .OrderBy(record => record.SequenceNumber)
            .ThenBy(record => record.CreatedUtc)
            .Take(Math.Max(0, maxCount))
            .ToArray();

        return Task.FromResult<IReadOnlyList<CabinetEventRecord>>(records);
    }

    private static string BuildCommandKey(Guid userId, Guid commandId, string idempotencyKey)
        => $"{userId:N}:{commandId:N}:{idempotencyKey}";

    private static string BuildCursorKey(Guid userId, int machineId)
        => $"{userId:N}:{machineId}";
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lucky5.Domain.Entities;
using Lucky5.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lucky5.Infrastructure.Data.Repositories;

public class EfCoreDataStore : IDataStore
{
    private readonly Lucky5DbContext _context;
    private readonly ILogger<EfCoreDataStore> _logger;

    public EfCoreDataStore(Lucky5DbContext context, ILogger<EfCoreDataStore> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _context.Users.FindAsync(userId);
    }

    public async Task UpdateUserAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

    public async Task<MemberProfile?> GetProfileAsync(Guid userId)
    {
        return await _context.Profiles.FindAsync(userId);
    }

    public async Task UpdateProfileAsync(MemberProfile profile)
    {
        _context.Profiles.Update(profile);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Machine>> GetMachinesAsync()
    {
        return await _context.Machines.ToListAsync();
    }

    public async Task<Machine?> GetMachineAsync(int machineId)
    {
        return await _context.Machines.FindAsync(machineId);
    }

    public async Task<List<Offer>> GetOffersAsync()
    {
        return await _context.Offers.ToListAsync();
    }

    public async Task<MachineSessionState?> GetMachineSessionAsync(Guid userId, int machineId)
    {
        return await _context.MachineSessions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.MachineId == machineId);
    }

    public async Task<MachineSessionState?> GetMachineSessionByIdAsync(Guid sessionId)
    {
        return await _context.MachineSessions.FindAsync(sessionId);
    }

    public async Task CreateMachineSessionAsync(MachineSessionState session)
    {
        _context.MachineSessions.Add(session);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateMachineSessionAsync(MachineSessionState session)
    {
        // Optimistic concurrency is handled by LastUpdatedUtc token
        session.LastUpdatedUtc = DateTime.UtcNow;
        _context.MachineSessions.Update(session);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict updating MachineSession {SessionId}", session.SessionId);
            throw new InvalidOperationException("The session was updated by another process. Please try again.", ex);
        }
    }

    public async Task DeleteMachineSessionAsync(Guid sessionId)
    {
        var session = await _context.MachineSessions.FindAsync(sessionId);
        if (session != null)
        {
            _context.MachineSessions.Remove(session);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<MachineLedgerState> GetOrInitializeMachineLedgerAsync(int machineId)
    {
        var ledger = await _context.MachineLedgers.FindAsync(machineId);
        if (ledger == null)
        {
            var machine = await _context.Machines.FindAsync(machineId);
            ledger = new MachineLedgerState
            {
                MachineId = machineId,
                MachineSerial = machine?.MachineSerial ?? string.Empty,
                MachineSerie = machine?.MachineSerie ?? string.Empty,
                MachineKent = machine?.MachineKent ?? string.Empty
            };
            _context.MachineLedgers.Add(ledger);
            await _context.SaveChangesAsync();
        }
        return ledger;
    }

    public async Task UpdateMachineLedgerAsync(MachineLedgerState ledger)
    {
        _context.MachineLedgers.Update(ledger);
        await _context.SaveChangesAsync();
    }

    public async Task<GameRound?> GetLatestRoundAsync(Guid userId, int machineId)
    {
        return await _context.GameRounds
            .Where(r => r.UserId == userId && r.MachineId == machineId)
            .OrderByDescending(r => r.CreatedUtc)
            .FirstOrDefaultAsync();
    }

    public async Task<GameRound?> GetRoundAsync(Guid roundId)
    {
        return await _context.GameRounds.FindAsync(roundId);
    }

    public async Task SaveRoundAsync(GameRound round)
    {
        var existing = await _context.GameRounds.FindAsync(round.RoundId);
        if (existing == null)
        {
            _context.GameRounds.Add(round);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(round);
            
            // EF Core Change tracking might not pick up deeply nested JSON property changes 
            // if the object reference itself didn't change, but we use value converters which 
            // serialize the entire object. We mark them modified to be safe.
            _context.Entry(existing).Property(x => x.InitialCards).IsModified = true;
            _context.Entry(existing).Property(x => x.FinalCards).IsModified = true;
            _context.Entry(existing).Property(x => x.CleanRoomState).IsModified = true;
            _context.Entry(existing).Property(x => x.DoubleUpSession).IsModified = true;
        }

        await _context.SaveChangesAsync();
    }

    public async Task AddWalletLedgerEntryAsync(WalletLedgerEntry entry)
    {
        _context.WalletLedgers.Add(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<CabinetCommandRecord?> GetCabinetCommandRecordAsync(Guid userId, Guid commandId, string idempotencyKey)
    {
        return await _context.CabinetCommandRecords
            .FirstOrDefaultAsync(record => record.UserId == userId
                && (record.CommandId == commandId || record.IdempotencyKey == idempotencyKey));
    }

    public async Task SaveCabinetCommandRecordAsync(CabinetCommandRecord record)
    {
        var existing = await GetCabinetCommandRecordAsync(record.UserId, record.CommandId, record.IdempotencyKey);
        if (existing is null)
        {
            _context.CabinetCommandRecords.Add(record);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(record);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<CabinetStateCursor> GetOrInitializeCabinetStateCursorAsync(Guid userId, int machineId)
    {
        var cursor = await _context.CabinetStateCursors.FindAsync(userId, machineId);
        if (cursor is not null)
        {
            return cursor;
        }

        cursor = new CabinetStateCursor
        {
            UserId = userId,
            MachineId = machineId
        };
        _context.CabinetStateCursors.Add(cursor);
        await _context.SaveChangesAsync();
        return cursor;
    }

    public async Task<CabinetStateCursor> AdvanceCabinetStateCursorAsync(Guid userId, int machineId)
    {
        var cursor = await GetOrInitializeCabinetStateCursorAsync(userId, machineId);
        cursor.StateVersion++;
        cursor.SequenceNumber++;
        cursor.UpdatedUtc = DateTime.UtcNow;
        _context.CabinetStateCursors.Update(cursor);
        await _context.SaveChangesAsync();
        return cursor;
    }

    public async Task SaveCabinetEventRecordAsync(CabinetEventRecord record)
    {
        _context.CabinetEventRecords.Add(record);
        await _context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<CabinetEventRecord>> GetCabinetEventRecordsAfterAsync(Guid userId, int machineId, long sequenceNumber, int maxCount)
    {
        return await _context.CabinetEventRecords
            .Where(record => record.UserId == userId
                && record.MachineId == machineId
                && record.SequenceNumber > sequenceNumber)
            .OrderBy(record => record.SequenceNumber)
            .ThenBy(record => record.CreatedUtc)
            .Take(Math.Max(0, maxCount))
            .ToListAsync();
    }
}

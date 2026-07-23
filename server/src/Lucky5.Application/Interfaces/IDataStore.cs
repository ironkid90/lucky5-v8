using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lucky5.Domain.Entities;

namespace Lucky5.Application.Interfaces;

public interface IDataStore
{
    Task<User?> GetUserByUsernameAsync(string username);
    Task<User?> GetUserByIdAsync(Guid userId);
    Task UpdateUserAsync(User user);
    Task CreateUserAsync(User user);
    Task<MemberProfile?> GetProfileAsync(Guid userId);
    Task UpdateProfileAsync(MemberProfile profile);
    Task CreateProfileAsync(MemberProfile profile);
    Task<IReadOnlyList<WalletLedgerEntry>> GetWalletLedgerEntriesAsync(Guid userId);

    Task<List<Agent>> GetAgentsAsync();
    Task<Agent?> GetAgentByIdAsync(int agentId);
        Task<Agent?> GetAgentByCodeAsync(string code);
        Task<Agent?> GetAgentByUserIdAsync(Guid userId);
        Task CreateAgentAsync(Agent agent);
        Task UpdateAgentAsync(Agent agent);

    Task<List<Machine>> GetMachinesAsync();
    Task<Machine?> GetMachineAsync(int machineId);
    Task<List<Offer>> GetOffersAsync();

    Task<MachineSessionState?> GetMachineSessionAsync(Guid userId, int machineId);
    Task<MachineSessionState?> GetMachineSessionByIdAsync(Guid sessionId);
    Task<List<MachineSessionState>> GetAllMachineSessionsAsync();
    Task CreateMachineSessionAsync(MachineSessionState session);
    Task UpdateMachineSessionAsync(MachineSessionState session);
    Task DeleteMachineSessionAsync(Guid sessionId);

    Task<MachineLedgerState> GetOrInitializeMachineLedgerAsync(int machineId);
    Task UpdateMachineLedgerAsync(MachineLedgerState ledger);

    Task<GameRound?> GetLatestRoundAsync(Guid userId, int machineId);
    Task<GameRound?> GetRoundAsync(Guid roundId);
    Task SaveRoundAsync(GameRound round);

    Task AddWalletLedgerEntryAsync(WalletLedgerEntry entry);

    Task<CabinetCommandRecord?> GetCabinetCommandRecordAsync(Guid userId, Guid commandId, string idempotencyKey);
    Task SaveCabinetCommandRecordAsync(CabinetCommandRecord record);
    Task<CabinetStateCursor> GetOrInitializeCabinetStateCursorAsync(Guid userId, int machineId);
    Task<CabinetStateCursor> AdvanceCabinetStateCursorAsync(Guid userId, int machineId);
    Task SaveCabinetEventRecordAsync(CabinetEventRecord record);
    Task<IReadOnlyList<CabinetEventRecord>> GetCabinetEventRecordsAfterAsync(Guid userId, int machineId, long sequenceNumber, int maxCount);

    // Token revocation
    Task<TokenRevocationEntry?> GetTokenRevocationAsync(string tokenHash, CancellationToken cancellationToken);
    Task SaveTokenRevocationAsync(TokenRevocationEntry entry, CancellationToken cancellationToken);
    Task DeleteTokenRevocationAsync(string tokenHash, CancellationToken cancellationToken);
    Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken);
    Task CleanupExpiredTokenRevocationsAsync(CancellationToken cancellationToken);
}

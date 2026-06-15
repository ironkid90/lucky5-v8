namespace Lucky5.Tests;

using Lucky5.Application.Contracts;
using Lucky5.Application.Requests;
using Lucky5.Domain.Entities;
using Lucky5.Infrastructure.Data.Repositories;
using Lucky5.Infrastructure.Services;

public sealed class MockEntropyGenerator(ulong fixedSeed) : IEntropyGenerator
{
    public ulong CreateSeed(Guid userId, int machineId, decimal betAmount, MachineLedgerState ledger)
        => fixedSeed;
}

public static class ReplayTests
{
    public static async Task RunAsync(List<string> failures)
    {
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var storeA = new InMemoryDataStore();
        var storeB = new InMemoryDataStore();
        var entropyA = new MockEntropyGenerator(0xCAFEBABE12345678UL);
        var entropyB = new MockEntropyGenerator(0xCAFEBABE12345678UL);
        var serviceA = new GameService(new InMemoryDataStoreAdapter(storeA), entropyA);
        var serviceB = new GameService(new InMemoryDataStoreAdapter(storeB), entropyB);

        SeedPlayer(storeA, userId, "replay-a", 2_000_000m);
        SeedPlayer(storeB, userId, "replay-b", 2_000_000m);

        var machineId = storeA.Machines.Values.First(machine => machine.IsOpen).Id;
        await serviceA.CashInAsync(userId, machineId, 200_000m, CancellationToken.None);
        await serviceB.CashInAsync(userId, machineId, 200_000m, CancellationToken.None);

        var dealA = await serviceA.DealAsync(userId, new DealRequest(machineId, storeA.Machines[machineId].MinBet), CancellationToken.None);
        var dealB = await serviceB.DealAsync(userId, new DealRequest(machineId, storeB.Machines[machineId].MinBet), CancellationToken.None);

        var handA = string.Join(",", dealA.Cards.Select(card => card.Code));
        var handB = string.Join(",", dealB.Cards.Select(card => card.Code));
        if (!string.Equals(handA, handB, StringComparison.Ordinal))
        {
            failures.Add($"Replay failure (Deal): expected {handA}, got {handB}");
        }

        var drawA = await serviceA.DrawAsync(userId, new DrawRequest(dealA.RoundId, []), CancellationToken.None);
        var drawB = await serviceB.DrawAsync(userId, new DrawRequest(dealB.RoundId, []), CancellationToken.None);

        var finalA = string.Join(",", drawA.Cards.Select(card => card.Code));
        var finalB = string.Join(",", drawB.Cards.Select(card => card.Code));
        if (!string.Equals(finalA, finalB, StringComparison.Ordinal))
        {
            failures.Add($"Replay failure (Draw): expected {finalA}, got {finalB}");
        }
    }

    private static void SeedPlayer(InMemoryDataStore store, Guid userId, string username, decimal walletBalance)
    {
        var user = new User
        {
            Id = userId,
            Username = username,
            PhoneNumber = $"+961{Math.Abs(username.GetHashCode()):0000000}",
            PasswordHash = "test-hash",
            IsOtpVerified = true,
            Role = "Player"
        };

        store.Profiles[userId] = user;
        store.Users[userId] = user;
        store.MemberProfiles[userId] = new MemberProfile
        {
            UserId = userId,
            Username = username,
            DisplayName = username,
            Email = $"{username}@lucky5.local",
            PhoneNumber = user.PhoneNumber,
            WalletBalance = walletBalance,
            LastSeenUtc = DateTime.UtcNow
        };
    }
}

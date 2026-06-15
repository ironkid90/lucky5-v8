using System.Text.Json;
using Lucky5.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Lucky5.Infrastructure.Data;

public class Lucky5DbContext : DbContext
{
    public Lucky5DbContext(DbContextOptions<Lucky5DbContext> options)
        : base(options)
    {
    }

    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<User> Users => Set<User>();
    public DbSet<MemberProfile> Profiles => Set<MemberProfile>();
    public DbSet<MachineSessionState> MachineSessions => Set<MachineSessionState>();
    public DbSet<MachineLedgerState> MachineLedgers => Set<MachineLedgerState>();
    public DbSet<GameRound> GameRounds => Set<GameRound>();
    public DbSet<WalletLedgerEntry> WalletLedgers => Set<WalletLedgerEntry>();
    public DbSet<CabinetCommandRecord> CabinetCommandRecords => Set<CabinetCommandRecord>();
    public DbSet<CabinetStateCursor> CabinetStateCursors => Set<CabinetStateCursor>();
    public DbSet<CabinetEventRecord> CabinetEventRecords => Set<CabinetEventRecord>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<ContactType> ContactTypes => Set<ContactType>();
    public DbSet<ContactReport> ContactReports => Set<ContactReport>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Machine Configuration
        modelBuilder.Entity<Machine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // Explicitly provided (e.g., 1, 2, 3)
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.MinBet).HasPrecision(18, 2);
            entity.Property(e => e.MaxBet).HasPrecision(18, 2);
        });

        // User Configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
        });

        // MemberProfile Configuration
        modelBuilder.Entity<MemberProfile>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.WalletBalance).HasPrecision(18, 2);

            // One-to-one relationship with User
            entity.HasOne<User>()
                .WithOne()
                .HasForeignKey<MemberProfile>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // MachineSessionState Configuration
        modelBuilder.Entity<MachineSessionState>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.HasIndex(e => new { e.MachineId, e.UserId });
            entity.Property(e => e.MachineCredits).HasPrecision(18, 2);
            entity.Property(e => e.TotalCashIn).HasPrecision(18, 2);

            // Concurrency token for optimistic concurrency (protecting concurrent credit updates)
            entity.Property(e => e.LastUpdatedUtc).IsConcurrencyToken();
        });

        // MachineLedgerState Configuration
        modelBuilder.Entity<MachineLedgerState>(entity =>
        {
            entity.HasKey(e => e.MachineId); // One-to-one with Machine conceptually
            entity.Property(e => e.MachineId).ValueGeneratedNever();

            entity.Property(e => e.TargetRtp).HasPrecision(18, 4);
            entity.Property(e => e.CapitalIn).HasPrecision(18, 2);
            entity.Property(e => e.CapitalOut).HasPrecision(18, 2);
            entity.Property(e => e.JackpotFullHouse).HasPrecision(18, 2);
            entity.Property(e => e.JackpotFourOfAKindA).HasPrecision(18, 2);
            entity.Property(e => e.JackpotFourOfAKindB).HasPrecision(18, 2);
            entity.Property(e => e.JackpotStraightFlush).HasPrecision(18, 2);
            entity.Property(e => e.BaseCapitalOut).HasPrecision(18, 2);
            entity.Property(e => e.JackpotCapitalOut).HasPrecision(18, 2);
            entity.Property(e => e.DoubleUpCapitalOut).HasPrecision(18, 2);
            entity.Property(e => e.LastPayoutScale).HasPrecision(18, 4);
            entity.Property(e => e.NetSinceLastClose).HasPrecision(18, 2);

            entity.Ignore(e => e.ObservedRtp); // Computed property
        });

        // GameRound Configuration
        modelBuilder.Entity<GameRound>(entity =>
        {
            entity.HasKey(e => e.RoundId);
            entity.HasIndex(e => new { e.MachineId, e.UserId });
            entity.HasIndex(e => e.CreatedUtc); // Helpful for history queries

            entity.Property(e => e.BetAmount).HasPrecision(18, 2);
            entity.Property(e => e.WinAmount).HasPrecision(18, 2);
            entity.Property(e => e.OriginalWinAmount).HasPrecision(18, 2);
            entity.Property(e => e.JackpotWinAmount).HasPrecision(18, 2);
            entity.Property(e => e.SettledAmount).HasPrecision(18, 2);
            entity.Property(e => e.HandRank).HasMaxLength(50);

            // Serialize complex state objects to JSONB for flexibility and long-term durability
            // without hard schema coupling to the exact domain types if they change slightly over time.
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            entity.Property(e => e.InitialCards)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, jsonOptions),
                      v => JsonSerializer.Deserialize<List<PokerCard>>(v, jsonOptions) ?? new List<PokerCard>())
                  .HasColumnType("jsonb")
                  .Metadata.SetValueComparer(new ValueComparer<List<PokerCard>>(
                      (c1, c2) => JsonSerializer.Serialize(c1, jsonOptions) == JsonSerializer.Serialize(c2, jsonOptions),
                      c => c.GetHashCode(),
                      c => JsonSerializer.Deserialize<List<PokerCard>>(JsonSerializer.Serialize(c, jsonOptions), jsonOptions)!));

            entity.Property(e => e.FinalCards)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, jsonOptions),
                      v => JsonSerializer.Deserialize<List<PokerCard>>(v, jsonOptions) ?? new List<PokerCard>())
                  .HasColumnType("jsonb")
                  .Metadata.SetValueComparer(new ValueComparer<List<PokerCard>>(
                      (c1, c2) => JsonSerializer.Serialize(c1, jsonOptions) == JsonSerializer.Serialize(c2, jsonOptions),
                      c => c.GetHashCode(),
                      c => JsonSerializer.Deserialize<List<PokerCard>>(JsonSerializer.Serialize(c, jsonOptions), jsonOptions)!));

            entity.Property(e => e.DoubleUpCard)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, jsonOptions),
                      v => JsonSerializer.Deserialize<PokerCard>(v, jsonOptions))
                  .HasColumnType("jsonb");

            entity.Property(e => e.CleanRoomState)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, jsonOptions),
                      v => JsonSerializer.Deserialize<Lucky5.Domain.Game.CleanRoom.FiveCardDrawState>(v, jsonOptions))
                  .HasColumnType("jsonb");

            entity.Property(e => e.DoubleUpSession)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, jsonOptions),
                      v => JsonSerializer.Deserialize<Lucky5.Domain.Game.CleanRoom.Lucky5DoubleUpSession>(v, jsonOptions))
                  .HasColumnType("jsonb");
        });

        // WalletLedgerEntry Configuration
        modelBuilder.Entity<WalletLedgerEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.BalanceAfter).HasPrecision(18, 2);
            entity.Property(e => e.TransactionType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ReferenceId).HasMaxLength(100);
        });

        modelBuilder.Entity<CabinetCommandRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.CommandId, e.IdempotencyKey }).IsUnique();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.CommandType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ResultJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CabinetStateCursor>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.MachineId });
            entity.Property(e => e.UpdatedUtc).IsConcurrencyToken();
        });

        modelBuilder.Entity<CabinetEventRecord>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.HasIndex(e => new { e.UserId, e.MachineId, e.SequenceNumber });
            entity.Property(e => e.EventType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PayloadJson).HasColumnType("jsonb");
        });

        // Seed Data for Machines
        modelBuilder.Entity<Machine>().HasData(
            new Machine { Id = 1, GameId = 1, Name = "Lucky 5 - Beirut", MinBet = 5000, MaxBet = 10000, IsOpen = true },
            new Machine { Id = 2, GameId = 1, Name = "Lucky 5 - Hamra", MinBet = 5000, MaxBet = 10000, IsOpen = true },
            new Machine { Id = 3, GameId = 1, Name = "Lucky 5 - VIP", MinBet = 5000, MaxBet = 10000, IsOpen = true }
        );

        // AppSetting Configuration
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
        });

        modelBuilder.Entity<AppSetting>().HasData(
            new AppSetting { Key = "game.houseRulesetVersion", Value = "v2" },
            new AppSetting { Key = "signalr.heartbeatSeconds", Value = "20" },
            new AppSetting { Key = "wallet.currency", Value = "USD" }
        );

        // Offers and Contacts Configuration
        modelBuilder.Entity<Offer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BonusAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Offer>().HasData(
            new Offer { Id = 1, Title = "Welcome Bonus", Description = "First deposit bonus", BonusAmount = 10 },
            new Offer { Id = 2, Title = "Weekend Cashback", Description = "5% cashback on losses", BonusAmount = 5 }
        );

        modelBuilder.Entity<ContactType>().HasData(
            new ContactType { Id = 1, Name = "Technical" },
            new ContactType { Id = 2, Name = "Billing" },
            new ContactType { Id = 3, Name = "General" }
        );
    }
}


using System.Text.Json;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CryptoTrendForge.Core.Infrastructure.Database;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Coin> Coins => Set<Coin>();
    public DbSet<Signal> Signals => Set<Signal>();
    public DbSet<SignalOutcome> SignalOutcomes => Set<SignalOutcome>();
    public DbSet<ScanNearMiss> ScanNearMisses => Set<ScanNearMiss>();
    public DbSet<BotConfiguration> BotConfigurations => Set<BotConfiguration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isNpgsql = Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        var jsonType = isNpgsql ? "jsonb" : "nvarchar(max)";
        var nowSql = isNpgsql ? "NOW()" : "GETUTCDATE()";
        var jsonDocumentConverter = new ValueConverter<JsonDocument?, string?>(
            value => value == null ? null : value.RootElement.GetRawText(),
            value => string.IsNullOrWhiteSpace(value)
                ? null
                : JsonDocument.Parse(value, new JsonDocumentOptions()));

        modelBuilder.Entity<Coin>(entity =>
        {
            entity.ToTable("coins");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Symbol).HasMaxLength(20).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(10).IsRequired();
            entity.Property(x => x.CoinType).HasConversion<int>().HasDefaultValue(CoinType.Crypto);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql(nowSql);
            entity.HasIndex(x => x.Symbol).IsUnique();
        });

        modelBuilder.Entity<Signal>(entity =>
        {
            entity.ToTable("signals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ScoreVersion).HasDefaultValue(0);
            entity.Property(x => x.Score).HasColumnType("numeric(5,2)");
            entity.Property(x => x.PatternBonus).HasDefaultValue(0);
            entity.Property(x => x.TotalScore).HasColumnType("numeric(5,2)");
            entity.Property(x => x.SignalType).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.MarketRegime).HasConversion<string>().HasMaxLength(10);
            entity.Property(x => x.SignalPrice).HasColumnType("numeric(18,8)");
            entity.Property(x => x.EntryPrice).HasColumnType("numeric(18,8)");
            entity.Property(x => x.StopPrice).HasColumnType("numeric(18,8)");
            entity.Property(x => x.CautiousExit).HasColumnType("numeric(18,8)");
            entity.Property(x => x.BalancedExit).HasColumnType("numeric(18,8)");
            entity.Property(x => x.WideExit).HasColumnType("numeric(18,8)");
            entity.Property(x => x.SupportLevel).HasColumnType("numeric(18,8)");
            entity.Property(x => x.SupportDistPct).HasColumnType("numeric(5,2)");
            entity.Property(x => x.FundingRate).HasColumnType("numeric(10,6)");
            entity.Property(x => x.Pattern1H4H).HasMaxLength(50);
            entity.Property(x => x.Pattern15M).HasMaxLength(50);
            entity.Property(x => x.BtcTrend).HasMaxLength(10);
            entity.Property(x => x.BtcEntryPrice).HasColumnType("numeric(18,8)");
            entity.Property(x => x.TargetsCloseReason).HasMaxLength(40);
            entity.Property(x => x.ScoreBreakdown)
                .HasConversion(jsonDocumentConverter)
                .HasColumnType(jsonType);
            entity.Property(x => x.Reasons)
                .HasConversion(jsonDocumentConverter)
                .HasColumnType(jsonType);
            entity.Property(x => x.Risks)
                .HasConversion(jsonDocumentConverter)
                .HasColumnType(jsonType);
            entity.Property(x => x.RawSnapshot)
                .HasConversion(jsonDocumentConverter)
                .HasColumnType(jsonType);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql(nowSql);
            entity.HasOne(x => x.Coin)
                .WithMany()
                .HasForeignKey(x => x.CoinId);
        });

        modelBuilder.Entity<SignalOutcome>(entity =>
        {
            entity.ToTable("signal_outcomes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PriceAt).HasColumnType("numeric(18,8)");
            entity.Property(x => x.PriceChangePct).HasColumnType("numeric(8,2)");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql(nowSql);
            entity.HasIndex(x => new { x.SignalId, x.MinutesElapsed }).IsUnique();
            entity.HasOne(x => x.Signal)
                .WithMany()
                .HasForeignKey(x => x.SignalId);
        });

        modelBuilder.Entity<ScanNearMiss>(entity =>
        {
            entity.ToTable("scan_near_misses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ScoreVersion).HasDefaultValue(0);
            entity.Property(x => x.TotalScore).HasColumnType("numeric(5,2)");
            entity.Property(x => x.BaseScore).HasColumnType("numeric(5,2)");
            entity.Property(x => x.BlockReason).HasMaxLength(300);
            entity.Property(x => x.Rsi4H).HasColumnType("numeric(8,2)");
            entity.Property(x => x.Ema20ExtensionPct).HasColumnType("numeric(8,2)");
            entity.Property(x => x.ScoreBreakdown)
                .HasConversion(jsonDocumentConverter)
                .HasColumnType(jsonType);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql(nowSql);
            entity.HasIndex(x => new { x.CoinId, x.CreatedAt });
            entity.HasOne(x => x.Coin)
                .WithMany()
                .HasForeignKey(x => x.CoinId);
        });

        modelBuilder.Entity<BotConfiguration>(entity =>
        {
            entity.ToTable("bot_configuration");
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql(nowSql);
        });
    }
}

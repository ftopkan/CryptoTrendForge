using CryptoTrendForge.Core.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace CryptoTrendForge.Core.Migrations.Postgres;

[DbContext(typeof(AppDbContext))]
partial class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.10");

        modelBuilder.Entity("CryptoTrendForge.Core.Domain.Models.BotConfiguration", b =>
        {
            b.Property<string>("Key")
                .HasColumnType("character varying(100)")
                .HasMaxLength(100);

            b.Property<DateTimeOffset>("UpdatedAt")
                .ValueGeneratedOnAdd()
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            b.Property<string>("Value")
                .IsRequired()
                .HasColumnType("text");

            b.HasKey("Key");
            b.ToTable("bot_configuration");
        });

        modelBuilder.Entity("CryptoTrendForge.Core.Domain.Models.Coin", b =>
        {
            b.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer");

            b.Property<DateTimeOffset>("CreatedAt")
                .ValueGeneratedOnAdd()
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            b.Property<string>("DisplayName")
                .IsRequired()
                .HasColumnType("character varying(10)")
                .HasMaxLength(10);

            b.Property<bool>("IsActive")
                .ValueGeneratedOnAdd()
                .HasColumnType("boolean")
                .HasDefaultValue(true);

            b.Property<string>("Symbol")
                .IsRequired()
                .HasColumnType("character varying(20)")
                .HasMaxLength(20);

            b.HasKey("Id");
            b.HasIndex("Symbol").IsUnique();
            b.ToTable("coins");
        });

        modelBuilder.Entity("CryptoTrendForge.Core.Domain.Models.Signal", b =>
        {
            b.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer");

            b.Property<string>("BtcTrend")
                .HasColumnType("character varying(10)")
                .HasMaxLength(10);

            b.Property<int>("CoinId")
                .HasColumnType("integer");

            b.Property<DateTimeOffset>("CreatedAt")
                .ValueGeneratedOnAdd()
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            b.Property<DateTimeOffset?>("ExpiresAt")
                .HasColumnType("timestamp with time zone");

            b.Property<decimal>("FundingRate")
                .HasColumnType("numeric(10,6)");

            b.Property<DateTimeOffset?>("InvalidatedAt")
                .HasColumnType("timestamp with time zone");

            b.Property<string>("MarketRegime")
                .IsRequired()
                .HasColumnType("character varying(10)")
                .HasMaxLength(10);

            b.Property<string>("Pattern15M")
                .HasColumnType("character varying(50)")
                .HasMaxLength(50);

            b.Property<string>("Pattern1H4H")
                .HasColumnType("character varying(50)")
                .HasMaxLength(50);

            b.Property<int>("PatternBonus")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer")
                .HasDefaultValue(0);

            b.Property<string>("RawSnapshot")
                .HasColumnType("jsonb");

            b.Property<string>("Reasons")
                .HasColumnType("jsonb");

            b.Property<string>("Risks")
                .HasColumnType("jsonb");

            b.Property<decimal>("Score")
                .HasColumnType("numeric(5,2)");

            b.Property<string>("ScoreBreakdown")
                .HasColumnType("jsonb");

            b.Property<decimal>("SignalPrice")
                .HasColumnType("numeric(18,8)");

            b.Property<string>("SignalType")
                .IsRequired()
                .HasColumnType("character varying(30)")
                .HasMaxLength(30);

            b.Property<string>("Status")
                .IsRequired()
                .HasColumnType("character varying(20)")
                .HasMaxLength(20);

            b.Property<decimal>("SupportDistPct")
                .HasColumnType("numeric(5,2)");

            b.Property<decimal>("SupportLevel")
                .HasColumnType("numeric(18,8)");

            b.Property<decimal>("TotalScore")
                .HasColumnType("numeric(5,2)");

            b.HasKey("Id");
            b.HasIndex("CoinId");
            b.ToTable("signals");
        });

        modelBuilder.Entity("CryptoTrendForge.Core.Domain.Models.SignalOutcome", b =>
        {
            b.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("integer");

            b.Property<DateTimeOffset>("CreatedAt")
                .ValueGeneratedOnAdd()
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("NOW()");

            b.Property<int>("MinutesElapsed")
                .HasColumnType("integer");

            b.Property<decimal>("PriceAt")
                .HasColumnType("numeric(18,8)");

            b.Property<decimal>("PriceChangePct")
                .HasColumnType("numeric(8,2)");

            b.Property<int>("SignalId")
                .HasColumnType("integer");

            b.Property<DateTimeOffset>("SnapshotAt")
                .HasColumnType("timestamp with time zone");

            b.HasKey("Id");
            b.HasIndex("SignalId", "MinutesElapsed").IsUnique();
            b.ToTable("signal_outcomes");
        });

        modelBuilder.Entity("CryptoTrendForge.Core.Domain.Models.Signal", b =>
        {
            b.HasOne("CryptoTrendForge.Core.Domain.Models.Coin", "Coin")
                .WithMany()
                .HasForeignKey("CoinId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("CryptoTrendForge.Core.Domain.Models.SignalOutcome", b =>
        {
            b.HasOne("CryptoTrendForge.Core.Domain.Models.Signal", "Signal")
                .WithMany()
                .HasForeignKey("SignalId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CryptoTrendForge.Core.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bot_configuration",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_configuration", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "coins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "signals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CoinId = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    PatternBonus = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TotalScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    SignalType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MarketRegime = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SignalPrice = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    SupportLevel = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    SupportDistPct = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    FundingRate = table.Column<decimal>(type: "numeric(10,6)", nullable: false),
                    Pattern1H4H = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Pattern15M = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BtcTrend = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ScoreBreakdown = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reasons = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Risks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    InvalidatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_signals_coins_CoinId",
                        column: x => x.CoinId,
                        principalTable: "coins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "signal_outcomes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SignalId = table.Column<int>(type: "int", nullable: false),
                    MinutesElapsed = table.Column<int>(type: "int", nullable: false),
                    PriceAt = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    PriceChangePct = table.Column<decimal>(type: "numeric(8,2)", nullable: false),
                    SnapshotAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signal_outcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_signal_outcomes_signals_SignalId",
                        column: x => x.SignalId,
                        principalTable: "signals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_coins_Symbol",
                table: "coins",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_signal_outcomes_SignalId_MinutesElapsed",
                table: "signal_outcomes",
                columns: new[] { "SignalId", "MinutesElapsed" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_signals_CoinId",
                table: "signals",
                column: "CoinId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_configuration");

            migrationBuilder.DropTable(
                name: "signal_outcomes");

            migrationBuilder.DropTable(
                name: "signals");

            migrationBuilder.DropTable(
                name: "coins");
        }
    }
}

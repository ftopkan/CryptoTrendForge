using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CryptoTrendForge.Core.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddNearMissAndStopTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScoreVersion",
                table: "signals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StopMinutes",
                table: "signals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StopReachedAt",
                table: "signals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scan_near_misses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoinId = table.Column<int>(type: "integer", nullable: false),
                    ScoreVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    BaseScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    PatternBonus = table.Column<int>(type: "integer", nullable: false),
                    CandidateThreshold = table.Column<int>(type: "integer", nullable: false),
                    BlockReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Rsi4H = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    Ema20ExtensionPct = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    ScoreBreakdown = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_near_misses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scan_near_misses_coins_CoinId",
                        column: x => x.CoinId,
                        principalTable: "coins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scan_near_misses_CoinId_CreatedAt",
                table: "scan_near_misses",
                columns: new[] { "CoinId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scan_near_misses");

            migrationBuilder.DropColumn(
                name: "ScoreVersion",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "StopMinutes",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "StopReachedAt",
                table: "signals");
        }
    }
}

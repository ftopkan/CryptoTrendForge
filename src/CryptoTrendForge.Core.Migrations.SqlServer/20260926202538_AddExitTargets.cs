using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CryptoTrendForge.Core.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddExitTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BalancedExit",
                table: "signals",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BalancedMinutes",
                table: "signals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BalancedReachedAt",
                table: "signals",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CautiousExit",
                table: "signals",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CautiousMinutes",
                table: "signals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CautiousReachedAt",
                table: "signals",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EntryPrice",
                table: "signals",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StopPrice",
                table: "signals",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TargetsClosedAt",
                table: "signals",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WideExit",
                table: "signals",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WideMinutes",
                table: "signals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WideReachedAt",
                table: "signals",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BalancedExit",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "BalancedMinutes",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "BalancedReachedAt",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "CautiousExit",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "CautiousMinutes",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "CautiousReachedAt",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "EntryPrice",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "StopPrice",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "TargetsClosedAt",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "WideExit",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "WideMinutes",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "WideReachedAt",
                table: "signals");
        }
    }
}

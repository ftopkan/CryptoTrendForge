using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CryptoTrendForge.Core.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddBtcEntryAndCloseReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BtcEntryPrice",
                table: "signals",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetsCloseReason",
                table: "signals",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BtcEntryPrice",
                table: "signals");

            migrationBuilder.DropColumn(
                name: "TargetsCloseReason",
                table: "signals");
        }
    }
}

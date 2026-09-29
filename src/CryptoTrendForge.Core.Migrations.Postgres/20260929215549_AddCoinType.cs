using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CryptoTrendForge.Core.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddCoinType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CoinType",
                table: "coins",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoinType",
                table: "coins");
        }
    }
}

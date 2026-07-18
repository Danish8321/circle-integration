using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryServiceOrchestrator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RedeemRequestFeesToJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeesAmount",
                table: "RedeemRequests");

            migrationBuilder.DropColumn(
                name: "FeesCurrencyCode",
                table: "RedeemRequests");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "RedeemRequests");

            migrationBuilder.DropColumn(
                name: "NetCurrencyCode",
                table: "RedeemRequests");

            migrationBuilder.AddColumn<string>(
                name: "FeesJson",
                table: "RedeemRequests",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetAmountJson",
                table: "RedeemRequests",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeesJson",
                table: "RedeemRequests");

            migrationBuilder.DropColumn(
                name: "NetAmountJson",
                table: "RedeemRequests");

            migrationBuilder.AddColumn<decimal>(
                name: "FeesAmount",
                table: "RedeemRequests",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeesCurrencyCode",
                table: "RedeemRequests",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "RedeemRequests",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetCurrencyCode",
                table: "RedeemRequests",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }
    }
}

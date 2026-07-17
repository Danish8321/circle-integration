using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryServiceOrchestrator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientCompanyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CircleTransferId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_CircleTransferId",
                table: "Transfers",
                column: "CircleTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_SubAccountId_ClientCompanyId",
                table: "Transfers",
                columns: new[] { "SubAccountId", "ClientCompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transfers");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryServiceOrchestrator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedBankAccountAndRedeemRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LinkedBankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientCompanyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BeneficiaryName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RoutingNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BillingName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BillingCity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BillingCountry = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    BillingLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BillingPostalCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BillingLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillingDistrict = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BankAddressCountry = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    BankAddressBankName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CircleBankAccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedBankAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RedeemRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientCompanyId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LinkedBankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CircleRedeemId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FeesAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    FeesCurrencyCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false),
                    GrossCurrencyCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: true),
                    NetCurrencyCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedeemRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LinkedBankAccounts_CircleBankAccountId",
                table: "LinkedBankAccounts",
                column: "CircleBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedBankAccounts_SubAccountId_ClientCompanyId",
                table: "LinkedBankAccounts",
                columns: new[] { "SubAccountId", "ClientCompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_RedeemRequests_CircleRedeemId",
                table: "RedeemRequests",
                column: "CircleRedeemId");

            migrationBuilder.CreateIndex(
                name: "IX_RedeemRequests_SubAccountId_ClientCompanyId",
                table: "RedeemRequests",
                columns: new[] { "SubAccountId", "ClientCompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkedBankAccounts");

            migrationBuilder.DropTable(
                name: "RedeemRequests");
        }
    }
}

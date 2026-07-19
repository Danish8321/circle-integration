using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryServiceOrchestrator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyReservationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ResultJson",
                table: "IdempotencyRecords",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "IdempotencyRecords",
                type: "datetime2",
                nullable: true);

            // Every pre-migration row is a terminal, completed operation (it carries a ResultJson),
            // so backfill Status to "Completed" — an empty-string default would not parse back to a
            // valid IdempotencyStatus. New rows always set Status explicitly (see IdempotencyService).
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "IdempotencyRecords",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Completed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "IdempotencyRecords");

            migrationBuilder.AlterColumn<string>(
                name: "ResultJson",
                table: "IdempotencyRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}

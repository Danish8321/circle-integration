using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreasuryServiceOrchestrator.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditRecordImmutabilityTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_AuditRecords_PreventUpdateDelete
                ON AuditRecords
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    RAISERROR('AuditRecords is append-only: UPDATE and DELETE are not permitted.', 16, 1);
                    ROLLBACK TRANSACTION;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER trg_AuditRecords_PreventUpdateDelete");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueTransactionHashPerAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Concurrent imports could store the same statement row more than once. Keep one copy per
            // account and hash (preferring a copy linked to a budget) so the unique index can be created.
            migrationBuilder.Sql("""
                DELETE FROM "Transactions" t
                USING (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "Account", "Hash"
                        ORDER BY ("BudgetId" IS NULL), "Date", "Id") AS copy
                    FROM "Transactions"
                ) d
                WHERE t."Id" = d."Id" AND d.copy > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Account_Hash",
                table: "Transactions",
                columns: new[] { "Account", "Hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_Account_Hash",
                table: "Transactions");
        }
    }
}

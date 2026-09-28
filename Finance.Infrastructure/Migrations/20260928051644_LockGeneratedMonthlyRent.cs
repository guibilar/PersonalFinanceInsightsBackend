using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LockGeneratedMonthlyRent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rent used to be generated as one "Monthly Rent" row a month; real payments are imported now. The rows
            // already stored stay (they are the only rent for months not re-imported), locked so re-applying the
            // category rules, which have no rule for that description, leaves them in Rent. An import that brings a
            // month's real payment hides that month's row (see LegacyMonthlyRent).
            migrationBuilder.Sql("""
                UPDATE "Transactions" SET "CategoryLocked" = TRUE
                WHERE "Account" = 'Chequing' AND "Category" = 'Rent' AND "Description" = 'Monthly Rent';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo safely: a row may also have been locked by hand.
        }
    }
}

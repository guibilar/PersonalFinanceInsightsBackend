using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPlanTransfer",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Wealthsimple rows imported before the flag: their description starts with the statement code.
            migrationBuilder.Sql("""
                UPDATE "Transactions" SET "IsPlanTransfer" = TRUE
                WHERE "Description" LIKE 'TRFINTF %' OR "Description" LIKE 'TRFOUTTF %' OR "Description" LIKE 'WIREINTF %'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPlanTransfer",
                table: "Transactions");
        }
    }
}

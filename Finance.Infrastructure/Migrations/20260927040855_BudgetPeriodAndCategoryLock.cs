using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BudgetPeriodAndCategoryLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CategoryLocked",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Enums are stored by name: existing budgets keep counting everything linked, i.e. "Total".
            migrationBuilder.AddColumn<string>(
                name: "Period",
                table: "Budgets",
                type: "text",
                nullable: false,
                defaultValue: "Total");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryLocked",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Period",
                table: "Budgets");
        }
    }
}

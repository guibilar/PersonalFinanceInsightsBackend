using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StoreEnumsAsStrings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-written: a plain type change would store '0', '1'... instead of the enum names.
            // Category: stored integer i becomes the name of enum member i (Dining, Entertainment, Groceries, Health, Other, Pet, Rent, Shopping, Subscriptions, Transportation, Utilities).
            migrationBuilder.Sql("""
                ALTER TABLE "Transactions" ALTER COLUMN "Category" TYPE text
                USING (ARRAY['Dining', 'Entertainment', 'Groceries', 'Health', 'Other', 'Pet', 'Rent', 'Shopping', 'Subscriptions', 'Transportation', 'Utilities'])["Category" + 1];
                """);

            // TransactionType: stored integer i becomes the name of enum member i (Credit, Debit).
            migrationBuilder.Sql("""
                ALTER TABLE "Transactions" ALTER COLUMN "TransactionType" TYPE text
                USING (ARRAY['Credit', 'Debit'])["TransactionType" + 1];
                """);

            // Account: stored integer i becomes the name of enum member i (Chequing, Savings, CreditCard).
            migrationBuilder.Sql("""
                ALTER TABLE "Transactions" ALTER COLUMN "Account" TYPE text
                USING (ARRAY['Chequing', 'Savings', 'CreditCard'])["Account" + 1];
                """);

            // Status: stored integer i becomes the name of enum member i (Active, Inactive).
            migrationBuilder.Sql("""
                ALTER TABLE "Budgets" ALTER COLUMN "Status" TYPE text
                USING (ARRAY['Active', 'Inactive'])["Status" + 1];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Transactions" ALTER COLUMN "Category" TYPE integer
                USING array_position(ARRAY['Dining', 'Entertainment', 'Groceries', 'Health', 'Other', 'Pet', 'Rent', 'Shopping', 'Subscriptions', 'Transportation', 'Utilities'], "Category") - 1;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Transactions" ALTER COLUMN "TransactionType" TYPE integer
                USING array_position(ARRAY['Credit', 'Debit'], "TransactionType") - 1;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Transactions" ALTER COLUMN "Account" TYPE integer
                USING array_position(ARRAY['Chequing', 'Savings', 'CreditCard'], "Account") - 1;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Budgets" ALTER COLUMN "Status" TYPE integer
                USING array_position(ARRAY['Active', 'Inactive'], "Status") - 1;
                """);
        }
    }
}

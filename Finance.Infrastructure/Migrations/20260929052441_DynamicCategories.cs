using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DynamicCategories : Migration
    {
        // The categories there were before categories were data, by the names the old enum stored. Written out here
        // rather than read from DefaultCategories: a migration has to keep doing what it did when it was written.
        private const string DefaultIds = """
            (VALUES
                ('Dining', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b01'::uuid),
                ('Entertainment', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b02'::uuid),
                ('Groceries', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b03'::uuid),
                ('Health', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b04'::uuid),
                ('Other', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b05'::uuid),
                ('Pet', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b06'::uuid),
                ('Rent', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b07'::uuid),
                ('Shopping', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b08'::uuid),
                ('Subscriptions', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b09'::uuid),
                ('Transportation', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b0a'::uuid),
                ('Utilities', '7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b0b'::uuid)
            ) AS defaults(name, id)
            """;

        private const string OtherId = "'7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b05'::uuid";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Color", "Name" },
                values: new object[,]
                {
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b01"), "#1F76B5", "Dining" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b02"), "#FF7E0F", "Entertainment" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b03"), "#2CA12D", "Groceries" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b04"), "#D72628", "Health" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b05"), "#9466BC", "Other" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b06"), "#8C564B", "Pet" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b07"), "#E277C3", "Rent" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b08"), "#7F7E7E", "Shopping" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b09"), "#BDBD23", "Subscriptions" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b0a"), "#16BFCF", "Transportation" },
                    { new Guid("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b0b"), "#800080", "Utilities" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            // Each row keeps its category: the stored name becomes that category's id. A name the enum never had
            // (there shouldn't be any) becomes Other, as an unknown value always read.
            foreach (var table in new[] { "Transactions", "CategoryRules" })
            {
                migrationBuilder.AddColumn<Guid>(name: "CategoryId", table: table, type: "uuid", nullable: true);
                migrationBuilder.Sql($"""
                    UPDATE "{table}" AS t SET "CategoryId" = defaults.id
                    FROM {DefaultIds}
                    WHERE t."Category" = defaults.name;
                    UPDATE "{table}" SET "CategoryId" = {OtherId} WHERE "CategoryId" IS NULL;
                    """);
                migrationBuilder.AlterColumn<Guid>(name: "CategoryId", table: table, type: "uuid", nullable: false,
                    oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
                migrationBuilder.DropColumn(name: "Category", table: table);
            }

            // A budget's categories: the key moves from the name to the id. An unknown name can't be kept (it would
            // repeat Other's key), so it is dropped: the budget simply stops counting it.
            migrationBuilder.AddColumn<Guid>(name: "CategoryId", table: "BudgetCategory", type: "uuid", nullable: true);
            migrationBuilder.Sql($"""
                UPDATE "BudgetCategory" AS b SET "CategoryId" = defaults.id
                FROM {DefaultIds}
                WHERE b."Category" = defaults.name;
                DELETE FROM "BudgetCategory" WHERE "CategoryId" IS NULL;
                """);
            migrationBuilder.DropPrimaryKey(name: "PK_BudgetCategory", table: "BudgetCategory");
            migrationBuilder.DropColumn(name: "Category", table: "BudgetCategory");
            migrationBuilder.AlterColumn<Guid>(name: "CategoryId", table: "BudgetCategory", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.AddPrimaryKey(name: "PK_BudgetCategory", table: "BudgetCategory", column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId",
                table: "Transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRules_CategoryId",
                table: "CategoryRules",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_BudgetCategory_Categories_CategoryId",
                table: "BudgetCategory",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CategoryRules_Categories_CategoryId",
                table: "CategoryRules",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Categories_CategoryId",
                table: "Transactions",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_BudgetCategory_Categories_CategoryId", table: "BudgetCategory");
            migrationBuilder.DropForeignKey(name: "FK_CategoryRules_Categories_CategoryId", table: "CategoryRules");
            migrationBuilder.DropForeignKey(name: "FK_Transactions_Categories_CategoryId", table: "Transactions");
            migrationBuilder.DropIndex(name: "IX_Transactions_CategoryId", table: "Transactions");
            migrationBuilder.DropIndex(name: "IX_CategoryRules_CategoryId", table: "CategoryRules");

            // Back to the enum's names: a default category by the name it had then, even if renamed since; a category
            // added in the app has no enum value, so its transactions and rules go to Other.
            foreach (var table in new[] { "Transactions", "CategoryRules" })
            {
                migrationBuilder.AddColumn<string>(name: "Category", table: table, type: "text", nullable: true);
                migrationBuilder.Sql($"""
                    UPDATE "{table}" AS t SET "Category" = defaults.name
                    FROM {DefaultIds}
                    WHERE t."CategoryId" = defaults.id;
                    UPDATE "{table}" SET "Category" = 'Other' WHERE "Category" IS NULL;
                    """);
                migrationBuilder.AlterColumn<string>(name: "Category", table: table, type: "text", nullable: false,
                    oldClrType: typeof(string), oldType: "text", oldNullable: true);
                migrationBuilder.DropColumn(name: "CategoryId", table: table);
            }

            // A budget can't keep a category the enum doesn't have (several would all become Other's key).
            migrationBuilder.AddColumn<string>(name: "Category", table: "BudgetCategory", type: "text", nullable: true);
            migrationBuilder.Sql($"""
                UPDATE "BudgetCategory" AS b SET "Category" = defaults.name
                FROM {DefaultIds}
                WHERE b."CategoryId" = defaults.id;
                DELETE FROM "BudgetCategory" WHERE "Category" IS NULL;
                """);
            migrationBuilder.DropPrimaryKey(name: "PK_BudgetCategory", table: "BudgetCategory");
            migrationBuilder.DropColumn(name: "CategoryId", table: "BudgetCategory");
            migrationBuilder.AlterColumn<string>(name: "Category", table: "BudgetCategory", type: "text", nullable: false,
                oldClrType: typeof(string), oldType: "text", oldNullable: true);
            migrationBuilder.AddPrimaryKey(name: "PK_BudgetCategory", table: "BudgetCategory", column: "Category");

            migrationBuilder.DropTable(name: "Categories");
        }
    }
}

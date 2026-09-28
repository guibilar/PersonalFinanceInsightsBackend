using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountsAndFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlowRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Keyword = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Flow = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowRules", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "Name", "Type" },
                values: new object[,]
                {
                    { new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a01"), "Chequing", "Chequing" },
                    { new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a02"), "Savings", "Savings" },
                    { new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a03"), "Credit card", "CreditCard" }
                });

            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Flow",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FlowLocked",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Each kind of account becomes the account of that kind, and every stored row gets the flow the reports
            // gave it until now: purchases were spending, money in was income on chequing and a refund on the card,
            // savings movements were never counted, and rows without a direction never showed up in reports.
            migrationBuilder.Sql("""
                UPDATE "Transactions" SET "AccountId" = CASE "Account"
                    WHEN 'Chequing' THEN '5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a01'::uuid
                    WHEN 'Savings' THEN '5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a02'::uuid
                    ELSE '5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a03'::uuid
                END;
                UPDATE "Transactions" SET "Flow" = CASE
                    WHEN "TransactionType" IS NULL THEN 'Ignored'
                    WHEN "Account" = 'Savings' THEN 'Transfer'
                    WHEN "TransactionType" = 'Debit' THEN 'Spending'
                    WHEN "Account" = 'CreditCard' THEN 'Refund'
                    ELSE 'Income'
                END;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                table: "Transactions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Flow",
                table: "Transactions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_Transactions_Account_Hash",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Account",
                table: "Transactions");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_Hash",
                table: "Transactions",
                columns: new[] { "AccountId", "Hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Name",
                table: "Accounts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlowRules_Keyword",
                table: "FlowRules",
                column: "Keyword",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Accounts_AccountId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId_Hash",
                table: "Transactions");

            migrationBuilder.AddColumn<string>(
                name: "Account",
                table: "Transactions",
                type: "text",
                nullable: true);

            // Back to one account per kind. Rows that only exist because every movement is imported now (chequing
            // deposits that aren't income, card payments, transfers) go, as before they were never stored. Two
            // accounts of the same kind holding the same statement row can't both stay: the second copy goes.
            migrationBuilder.Sql("""
                UPDATE "Transactions" t SET "Account" = a."Type" FROM "Accounts" a WHERE a."Id" = t."AccountId";
                DELETE FROM "Transactions"
                    WHERE ("Account" <> 'Savings' AND "TransactionType" IS NOT NULL AND "Flow" IN ('Transfer', 'Ignored'))
                       OR ("Account" = 'Chequing' AND "TransactionType" = 'Credit' AND "Flow" <> 'Income');
                DELETE FROM "Transactions" t USING "Transactions" other
                    WHERE t."Account" = other."Account" AND t."Hash" = other."Hash" AND t."Id" > other."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Account",
                table: "Transactions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "AccountId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Flow",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "FlowLocked",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "FlowRules");

            migrationBuilder.Sql("""DELETE FROM "SeedMarkers" WHERE "Name" = 'FlowRules';""");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Account_Hash",
                table: "Transactions",
                columns: new[] { "Account", "Hash" },
                unique: true);
        }
    }
}

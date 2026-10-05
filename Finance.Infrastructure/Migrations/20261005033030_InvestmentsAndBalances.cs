using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvestmentsAndBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ContributionRoom",
                table: "Accounts",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContributionRoomYear",
                table: "Accounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Accounts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "AccountBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountBalances_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a01"),
                columns: new[] { "ContributionRoom", "ContributionRoomYear" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a02"),
                columns: new[] { "ContributionRoom", "ContributionRoomYear" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a03"),
                columns: new[] { "ContributionRoom", "ContributionRoomYear" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_AccountBalances_AccountId_Date",
                table: "AccountBalances",
                columns: new[] { "AccountId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountBalances");

            migrationBuilder.DropColumn(
                name: "ContributionRoom",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ContributionRoomYear",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Accounts");
        }
    }
}

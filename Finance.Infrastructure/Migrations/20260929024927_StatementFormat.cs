using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StatementFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "Accounts",
                type: "text",
                nullable: false,
                // Every account so far imports CIBC files, the only format there was.
                defaultValue: "Cibc");

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a01"),
                column: "Format",
                value: "Cibc");

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a02"),
                column: "Format",
                value: "Cibc");

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a03"),
                column: "Format",
                value: "Cibc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Format",
                table: "Accounts");
        }
    }
}

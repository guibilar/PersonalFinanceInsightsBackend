using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountLastImportedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastImportedAt",
                table: "Accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a01"),
                column: "LastImportedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a02"),
                column: "LastImportedAt",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: new Guid("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a03"),
                column: "LastImportedAt",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastImportedAt",
                table: "Accounts");
        }
    }
}

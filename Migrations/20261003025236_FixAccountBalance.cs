using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CopyTrading.Migrations
{
    /// <inheritdoc />
    public partial class FixAccountBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "C001",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 22, 35, 766, DateTimeKind.Local).AddTicks(9815));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "C002",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 22, 35, 766, DateTimeKind.Local).AddTicks(9816));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "C003",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 22, 35, 766, DateTimeKind.Local).AddTicks(9817));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "C004",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 22, 35, 766, DateTimeKind.Local).AddTicks(9818));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "P001",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 22, 35, 766, DateTimeKind.Local).AddTicks(9802));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "C001",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4973));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "C002",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4975));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "C003",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4976));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "C004",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4980));

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "AccountId",
                keyValue: "P001",
                column: "CreatedAt",
                value: new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4922));
        }
    }
}

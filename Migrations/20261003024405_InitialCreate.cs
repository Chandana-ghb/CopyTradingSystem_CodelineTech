using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CopyTrading.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    AccountId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccountType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.AccountId);
                });

            migrationBuilder.CreateTable(
                name: "ChildOrders",
                columns: table => new
                {
                    ChildOrderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentOrderId = table.Column<int>(type: "int", nullable: false),
                    ChildAccountId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChildAccountName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrderType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    OrderStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReplicatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChildOrders", x => x.ChildOrderId);
                });

            migrationBuilder.CreateTable(
                name: "ParentOrders",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentAccountId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrderType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    OrderStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlacedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParentOrders", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "AccountMappings",
                columns: table => new
                {
                    MappingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentAccountId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChildAccountId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    QtyMultiplier = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountMappings", x => x.MappingId);
                    table.ForeignKey(
                        name: "FK_AccountMappings_Accounts_ChildAccountId",
                        column: x => x.ChildAccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountMappings_Accounts_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "AccountId", "AccountName", "AccountType", "Balance", "CreatedAt" },
                values: new object[,]
                {
                    { "C001", "Ramu (Child 1)", "CHILD", 500000.00m, new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4973) },
                    { "C002", "Seenu (Child 2)", "CHILD", 500000.00m, new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4975) },
                    { "C003", "Priya (Child 3)", "CHILD", 500000.00m, new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4976) },
                    { "C004", "Arjun (Child 4)", "CHILD", 500000.00m, new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4980) },
                    { "P001", "Chandana (Parent)", "PARENT", 1000000.00m, new DateTime(2026, 10, 3, 8, 14, 4, 352, DateTimeKind.Local).AddTicks(4922) }
                });

            migrationBuilder.InsertData(
                table: "AccountMappings",
                columns: new[] { "MappingId", "ChildAccountId", "IsActive", "ParentAccountId", "QtyMultiplier" },
                values: new object[,]
                {
                    { 1, "C001", true, "P001", 1.0m },
                    { 2, "C002", true, "P001", 0.5m },
                    { 3, "C003", true, "P001", 2.0m },
                    { 4, "C004", true, "P001", 1.5m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountMappings_ChildAccountId",
                table: "AccountMappings",
                column: "ChildAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountMappings_ParentAccountId",
                table: "AccountMappings",
                column: "ParentAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountMappings");

            migrationBuilder.DropTable(
                name: "ChildOrders");

            migrationBuilder.DropTable(
                name: "ParentOrders");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}

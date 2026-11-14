using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oylik.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultiSiteEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "EmployeesSyncedAt",
                table: "Settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TabelBase",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SyncHash",
                table: "Employees",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeTombstones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TabelNo = table.Column<int>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeTombstones", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeTombstones");

            migrationBuilder.DropColumn(
                name: "EmployeesSyncedAt",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TabelBase",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SyncHash",
                table: "Employees");
        }
    }
}

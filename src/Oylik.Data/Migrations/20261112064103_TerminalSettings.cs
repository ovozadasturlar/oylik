using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oylik.Data.Migrations
{
    /// <inheritdoc />
    public partial class TerminalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TerminalEnabled",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "TerminalLastSerial",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "TerminalPassword",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TerminalRequireConfirm",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TerminalUrl",
                table: "Settings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerminalEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TerminalLastSerial",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TerminalPassword",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TerminalRequireConfirm",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TerminalUrl",
                table: "Settings");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oylik.Data.Migrations
{
    /// <inheritdoc />
    public partial class TerminalSerial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TerminalSerial",
                table: "Settings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TerminalSerial",
                table: "Settings");
        }
    }
}

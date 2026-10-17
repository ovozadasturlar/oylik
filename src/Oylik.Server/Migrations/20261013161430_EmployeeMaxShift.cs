using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oylik.Server.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeMaxShift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxShiftMinutes",
                table: "Employees",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxShiftMinutes",
                table: "Employees");
        }
    }
}

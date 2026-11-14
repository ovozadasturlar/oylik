using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oylik.Server.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeTabelNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TabelNo",
                table: "Employees",
                type: "TEXT",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TabelNo",
                table: "Employees");
        }
    }
}

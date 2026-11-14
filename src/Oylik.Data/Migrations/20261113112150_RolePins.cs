using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oylik.Data.Migrations
{
    /// <inheritdoc />
    public partial class RolePins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RolePins",
                columns: table => new
                {
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Salt = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Hash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Iterations = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePins", x => x.Role);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePins");
        }
    }
}

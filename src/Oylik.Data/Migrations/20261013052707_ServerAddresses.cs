using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oylik.Data.Migrations
{
    /// <inheritdoc />
    public partial class ServerAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocalServerUrl",
                table: "Settings",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            // 6.1: eski bitta manzil — http:// va IP yoki localhost bo'lsa lokal, aks holda global (ServerUrl'da qoladi).
            // ServerConnection.IsLocalAddress bilan bir xil qoida.
            migrationBuilder.Sql("""
                UPDATE Settings
                SET LocalServerUrl = ServerUrl, ServerUrl = NULL
                WHERE ServerUrl IS NOT NULL
                  AND (lower(ServerUrl) GLOB 'http://[0-9]*'
                       OR lower(ServerUrl) GLOB 'http://localhost*'
                       OR lower(ServerUrl) GLOB 'http://[[]*');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Settings SET ServerUrl = COALESCE(ServerUrl, LocalServerUrl);");

            migrationBuilder.DropColumn(
                name: "LocalServerUrl",
                table: "Settings");
        }
    }
}

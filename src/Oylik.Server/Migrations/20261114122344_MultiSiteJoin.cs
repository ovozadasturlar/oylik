using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oylik.Server.Migrations
{
    /// <inheritdoc />
    public partial class MultiSiteJoin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bosqich 3: kalitlar alohida jadvalga (o'rnatmaga 1:ko'p). Avval jadval, keyin mavjud kalitlarni ko'chirish
            // (asosiy mashina, SiteIndex 0; Id = o'rnatma Id), shundan keyingina eski ustun o'chiriladi — kalit yo'qolmaydi.
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SiteIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.Sql(
                "INSERT INTO ApiKeys (Id, InstallationId, KeyHash, Name, SiteIndex, CreatedAt) " +
                "SELECT Id, Id, ApiKeyHash, Name, 0, CreatedAt FROM Installations;");

            migrationBuilder.DropIndex(
                name: "IX_Installations_ApiKeyHash",
                table: "Installations");

            migrationBuilder.DropColumn(
                name: "ApiKeyHash",
                table: "Installations");

            migrationBuilder.AddColumn<int>(
                name: "JoinCount",
                table: "Installations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "JoinCodes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    InstallationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UsedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JoinCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_InstallationId",
                table: "ApiKeys",
                column: "InstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JoinCodes_InstallationId_Code",
                table: "JoinCodes",
                columns: new[] { "InstallationId", "Code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JoinCodes");

            migrationBuilder.DropColumn(
                name: "JoinCount",
                table: "Installations");

            migrationBuilder.AddColumn<string>(
                name: "ApiKeyHash",
                table: "Installations",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Orqaga: faqat asosiy mashina kaliti qaytadi (join qilingan mashinalar kaliti yo'qoladi).
            migrationBuilder.Sql(
                "UPDATE Installations SET ApiKeyHash = (SELECT KeyHash FROM ApiKeys k " +
                "WHERE k.InstallationId = Installations.Id AND k.SiteIndex = 0 ORDER BY k.CreatedAt LIMIT 1);");

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.CreateIndex(
                name: "IX_Installations_ApiKeyHash",
                table: "Installations",
                column: "ApiKeyHash",
                unique: true);
        }
    }
}

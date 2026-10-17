using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oylik.Data.Migrations
{
    /// <inheritdoc />
    public partial class Attendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastEventSeq",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LastSyncAt",
                table: "Settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReceivedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ServerSeq = table.Column<long>(type: "INTEGER", nullable: true),
                    IsVoided = table.Column<bool>(type: "INTEGER", nullable: false),
                    VoidedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PendingPush = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PairedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEvents_EmployeeId_OccurredAt",
                table: "AttendanceEvents",
                columns: new[] { "EmployeeId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEvents_OccurredAt",
                table: "AttendanceEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEvents_PendingPush",
                table: "AttendanceEvents",
                column: "PendingPush");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEvents_ServerSeq",
                table: "AttendanceEvents",
                column: "ServerSeq");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceEvents");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropColumn(
                name: "LastEventSeq",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LastSyncAt",
                table: "Settings");
        }
    }
}

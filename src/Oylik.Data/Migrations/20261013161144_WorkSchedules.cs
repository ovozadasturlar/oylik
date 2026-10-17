using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Oylik.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AbsentAfterMinutes",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "GreenPercent",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 95);

            migrationBuilder.AddColumn<int>(
                name: "LateGraceMinutes",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "OvertimePolicy",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeValue",
                table: "Settings",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "YellowPercent",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 75);

            migrationBuilder.AddColumn<long>(
                name: "BasePay",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "EarlyLeaveMinutes",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GraceMinutes",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LateMinutes",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeMinutes",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "OvertimePay",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "OvertimePolicy",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeValue",
                table: "Lines",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PayMode",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlannedMinutes",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkedMinutes",
                table: "Lines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OvertimePolicy",
                table: "Employees",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OvertimeValue",
                table: "Employees",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayMode",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TabelNo",
                table: "Employees",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ExcusedDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcusedDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CycleAnchor = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IsOff = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    BreakMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    WeekDays = table.Column<int>(type: "INTEGER", nullable: false),
                    CycleWorkDays = table.Column<int>(type: "INTEGER", nullable: false),
                    CycleOffDays = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    BreakMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Schedules",
                columns: new[] { "Id", "BreakMinutes", "CycleOffDays", "CycleWorkDays", "DurationMinutes", "Kind", "Name", "SortOrder", "StartTime", "WeekDays" },
                values: new object[,]
                {
                    { new Guid("5c1e0b7a-3d2f-4e8a-9b61-0a7d2c4e1f01"), 60, 1, 1, 540, 0, "Kunduzgi 08:00–17:00", 1, new TimeOnly(8, 0, 0), 126 },
                    { new Guid("5c1e0b7a-3d2f-4e8a-9b61-0a7d2c4e1f02"), 0, 2, 2, 720, 1, "12 soat 08:00–20:00", 2, new TimeOnly(8, 0, 0), 0 },
                    { new Guid("5c1e0b7a-3d2f-4e8a-9b61-0a7d2c4e1f03"), 0, 1, 1, 1440, 1, "Sutkalik 08:00–08:00, kun ora", 3, new TimeOnly(8, 0, 0), 0 }
                });

            // H13.10: mavjud xodimlarga doimiy tabel raqami — yaratilish tartibida 1..n (bir xil vaqtda — Id bo'yicha)
            migrationBuilder.Sql("""
                UPDATE Employees
                SET TabelNo = (SELECT COUNT(*) FROM Employees e2
                               WHERE e2.CreatedAt < Employees.CreatedAt
                                  OR (e2.CreatedAt = Employees.CreatedAt AND e2.Id <= Employees.Id));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TabelNo",
                table: "Employees",
                column: "TabelNo");

            migrationBuilder.CreateIndex(
                name: "IX_ExcusedDays_EmployeeId_Date",
                table: "ExcusedDays",
                columns: new[] { "EmployeeId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleAssignments_EmployeeId_EffectiveFrom",
                table: "ScheduleAssignments",
                columns: new[] { "EmployeeId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleOverrides_EmployeeId_Date",
                table: "ScheduleOverrides",
                columns: new[] { "EmployeeId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExcusedDays");

            migrationBuilder.DropTable(
                name: "ScheduleAssignments");

            migrationBuilder.DropTable(
                name: "ScheduleOverrides");

            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropIndex(
                name: "IX_Employees_TabelNo",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "AbsentAfterMinutes",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "GreenPercent",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LateGraceMinutes",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "OvertimePolicy",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "OvertimeValue",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "YellowPercent",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "BasePay",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "EarlyLeaveMinutes",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "GraceMinutes",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "LateMinutes",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "OvertimeMinutes",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "OvertimePay",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "OvertimePolicy",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "OvertimeValue",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "PayMode",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "PlannedMinutes",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "WorkedMinutes",
                table: "Lines");

            migrationBuilder.DropColumn(
                name: "OvertimePolicy",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "OvertimeValue",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PayMode",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TabelNo",
                table: "Employees");
        }
    }
}

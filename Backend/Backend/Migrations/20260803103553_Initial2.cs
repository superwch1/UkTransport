using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class Initial2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArrivalDayOffset",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "DepartureDayOffset",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "ScheduledDayOffset",
                table: "BusCallingPoints");

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "DepartureTime",
                table: "BusTimetables",
                type: "interval",
                nullable: false,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone");

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "ArrivalTime",
                table: "BusTimetables",
                type: "interval",
                nullable: false,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone");

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "ScheduledTime",
                table: "BusCallingPoints",
                type: "interval",
                nullable: false,
                oldClrType: typeof(TimeOnly),
                oldType: "time without time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<TimeOnly>(
                name: "DepartureTime",
                table: "BusTimetables",
                type: "time without time zone",
                nullable: false,
                oldClrType: typeof(TimeSpan),
                oldType: "interval");

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "ArrivalTime",
                table: "BusTimetables",
                type: "time without time zone",
                nullable: false,
                oldClrType: typeof(TimeSpan),
                oldType: "interval");

            migrationBuilder.AddColumn<int>(
                name: "ArrivalDayOffset",
                table: "BusTimetables",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DepartureDayOffset",
                table: "BusTimetables",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<TimeOnly>(
                name: "ScheduledTime",
                table: "BusCallingPoints",
                type: "time without time zone",
                nullable: false,
                oldClrType: typeof(TimeSpan),
                oldType: "interval");

            migrationBuilder.AddColumn<int>(
                name: "ScheduledDayOffset",
                table: "BusCallingPoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}

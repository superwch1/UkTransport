using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class Initial5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TripScheduleKey",
                table: "BusTimetables",
                newName: "TripJourneyKey");

            migrationBuilder.RenameIndex(
                name: "IX_BusTimetables_TripScheduleKey",
                table: "BusTimetables",
                newName: "IX_BusTimetables_TripJourneyKey");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DepartureTime",
                table: "BusTimetables",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_DepartureTime",
                table: "BusTimetables",
                column: "DepartureTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_DepartureTime",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "DepartureTime",
                table: "BusTimetables");

            migrationBuilder.RenameColumn(
                name: "TripJourneyKey",
                table: "BusTimetables",
                newName: "TripScheduleKey");

            migrationBuilder.RenameIndex(
                name: "IX_BusTimetables_TripJourneyKey",
                table: "BusTimetables",
                newName: "IX_BusTimetables_TripScheduleKey");
        }
    }
}

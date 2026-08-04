using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class Initial3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_DestinationBusStopId",
                table: "BusTimetables");

            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_LineName",
                table: "BusTimetables");

            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_OperatorId",
                table: "BusTimetables");

            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_OriginBusStopId",
                table: "BusTimetables");

            migrationBuilder.AddColumn<string>(
                name: "DestinationName",
                table: "BusTimetables",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OriginName",
                table: "BusTimetables",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RouteDescription",
                table: "BusTimetables",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsImportCompleted",
                table: "BusDatasets",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationName",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "OriginName",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "RouteDescription",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "IsImportCompleted",
                table: "BusDatasets");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_DestinationBusStopId",
                table: "BusTimetables",
                column: "DestinationBusStopId");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_LineName",
                table: "BusTimetables",
                column: "LineName");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_OperatorId",
                table: "BusTimetables",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_OriginBusStopId",
                table: "BusTimetables",
                column: "OriginBusStopId");
        }
    }
}

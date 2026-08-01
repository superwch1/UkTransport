using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class Initial4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LineName",
                table: "BusCallingPoints");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "BusCallingPoints");

            migrationBuilder.RenameColumn(
                name: "OriginName",
                table: "BusTimetables",
                newName: "OriginBusStopId");

            migrationBuilder.RenameColumn(
                name: "DestinationName",
                table: "BusTimetables",
                newName: "DestinationBusStopId");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_DestinationBusStopId",
                table: "BusTimetables",
                column: "DestinationBusStopId");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_OriginBusStopId",
                table: "BusTimetables",
                column: "OriginBusStopId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_DestinationBusStopId",
                table: "BusTimetables");

            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_OriginBusStopId",
                table: "BusTimetables");

            migrationBuilder.RenameColumn(
                name: "OriginBusStopId",
                table: "BusTimetables",
                newName: "OriginName");

            migrationBuilder.RenameColumn(
                name: "DestinationBusStopId",
                table: "BusTimetables",
                newName: "DestinationName");

            migrationBuilder.AddColumn<string>(
                name: "LineName",
                table: "BusCallingPoints",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OperatorId",
                table: "BusCallingPoints",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}

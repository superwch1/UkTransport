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
            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_LineName",
                table: "BusTimetables",
                column: "LineName");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_OperatorRef",
                table: "BusTimetables",
                column: "OperatorRef");

            migrationBuilder.CreateIndex(
                name: "IX_BusCallingPoints_BusStopId",
                table: "BusCallingPoints",
                column: "BusStopId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_LineName",
                table: "BusTimetables");

            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_OperatorRef",
                table: "BusTimetables");

            migrationBuilder.DropIndex(
                name: "IX_BusCallingPoints_BusStopId",
                table: "BusCallingPoints");
        }
    }
}

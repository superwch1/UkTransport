using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class Initial6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CountryCount",
                table: "BusTimetables",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "EastMidlands",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EastOfEngland",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "London",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NorthEast",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NorthWest",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NorthernIreland",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Scotland",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SouthEast",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SouthWest",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StopPatternKey",
                table: "BusTimetables",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Wales",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WestMidlands",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "YorkshireAndTheHumber",
                table: "BusTimetables",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "BusCallingPoints",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "BusCallingPoints",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_StopPatternKey",
                table: "BusTimetables",
                column: "StopPatternKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusTimetables_StopPatternKey",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "CountryCount",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "EastMidlands",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "EastOfEngland",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "London",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "NorthEast",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "NorthWest",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "NorthernIreland",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "Scotland",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "SouthEast",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "SouthWest",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "StopPatternKey",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "Wales",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "WestMidlands",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "YorkshireAndTheHumber",
                table: "BusTimetables");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "BusCallingPoints");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "BusCallingPoints");
        }
    }
}

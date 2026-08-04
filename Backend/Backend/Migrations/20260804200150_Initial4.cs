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
                name: "RouteDescription",
                table: "BusTimetables");

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "BusCallingPoints",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "BusCallingPoints",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "BusCallingPoints",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "BusCallingPoints");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "BusCallingPoints");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "BusCallingPoints");

            migrationBuilder.AddColumn<string>(
                name: "RouteDescription",
                table: "BusTimetables",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}

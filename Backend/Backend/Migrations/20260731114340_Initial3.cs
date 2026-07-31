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
            migrationBuilder.RenameColumn(
                name: "NationalOperatorRef",
                table: "BusTimetables",
                newName: "OperatorId");

            migrationBuilder.RenameIndex(
                name: "IX_BusTimetables_NationalOperatorRef",
                table: "BusTimetables",
                newName: "IX_BusTimetables_OperatorId");

            migrationBuilder.RenameColumn(
                name: "OperatorRef",
                table: "BusCallingPoints",
                newName: "OperatorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OperatorId",
                table: "BusTimetables",
                newName: "NationalOperatorRef");

            migrationBuilder.RenameIndex(
                name: "IX_BusTimetables_OperatorId",
                table: "BusTimetables",
                newName: "IX_BusTimetables_NationalOperatorRef");

            migrationBuilder.RenameColumn(
                name: "OperatorId",
                table: "BusCallingPoints",
                newName: "OperatorRef");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class Initial1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusTimetables",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OperatorRef = table.Column<string>(type: "text", nullable: false),
                    LineName = table.Column<string>(type: "text", nullable: false),
                    OriginName = table.Column<string>(type: "text", nullable: false),
                    DestinationName = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Monday = table.Column<bool>(type: "boolean", nullable: false),
                    Tuesday = table.Column<bool>(type: "boolean", nullable: false),
                    Wednesday = table.Column<bool>(type: "boolean", nullable: false),
                    Thursday = table.Column<bool>(type: "boolean", nullable: false),
                    Friday = table.Column<bool>(type: "boolean", nullable: false),
                    Saturday = table.Column<bool>(type: "boolean", nullable: false),
                    Sunday = table.Column<bool>(type: "boolean", nullable: false),
                    RunsOnBankHolidays = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusTimetables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusCallingPoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusTimetableId = table.Column<string>(type: "text", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    BusStopId = table.Column<string>(type: "text", nullable: false),
                    ArrivalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    DepartureTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ArrivalDayOffset = table.Column<int>(type: "integer", nullable: true),
                    DepartureDayOffset = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusCallingPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusCallingPoints_BusTimetables_BusTimetableId",
                        column: x => x.BusTimetableId,
                        principalTable: "BusTimetables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusCallingPoints_BusTimetableId",
                table: "BusCallingPoints",
                column: "BusTimetableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusCallingPoints");

            migrationBuilder.DropTable(
                name: "BusTimetables");
        }
    }
}

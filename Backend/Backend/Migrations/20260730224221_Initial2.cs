using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class Initial2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusDatasets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusDatasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PublicHolidays",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicHolidays", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "BusTimetables",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DatasetId = table.Column<string>(type: "text", nullable: false),
                    NationalOperatorRef = table.Column<string>(type: "text", nullable: false),
                    OperatorName = table.Column<string>(type: "text", nullable: false),
                    LineName = table.Column<string>(type: "text", nullable: false),
                    OriginName = table.Column<string>(type: "text", nullable: false),
                    DestinationName = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    ScheduledDayOffset = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TripScheduleKey = table.Column<string>(type: "text", nullable: false),
                    Monday = table.Column<bool>(type: "boolean", nullable: false),
                    Tuesday = table.Column<bool>(type: "boolean", nullable: false),
                    Wednesday = table.Column<bool>(type: "boolean", nullable: false),
                    Thursday = table.Column<bool>(type: "boolean", nullable: false),
                    Friday = table.Column<bool>(type: "boolean", nullable: false),
                    Saturday = table.Column<bool>(type: "boolean", nullable: false),
                    Sunday = table.Column<bool>(type: "boolean", nullable: false),
                    WeeksOfMonth = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusTimetables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusTimetables_BusDatasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "BusDatasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                    LineName = table.Column<string>(type: "text", nullable: false),
                    OperatorRef = table.Column<string>(type: "text", nullable: false),
                    ScheduledTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ScheduledDayOffset = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "BusHolidays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusTimetableId = table.Column<string>(type: "text", nullable: false),
                    PublicHolidayName = table.Column<string>(type: "text", nullable: false),
                    IsOperating = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusHolidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusHolidays_BusTimetables_BusTimetableId",
                        column: x => x.BusTimetableId,
                        principalTable: "BusTimetables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BusHolidays_PublicHolidays_PublicHolidayName",
                        column: x => x.PublicHolidayName,
                        principalTable: "PublicHolidays",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BusSpecialDays",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusTimetableId = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsOperating = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusSpecialDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusSpecialDays_BusTimetables_BusTimetableId",
                        column: x => x.BusTimetableId,
                        principalTable: "BusTimetables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusCallingPoints_BusStopId",
                table: "BusCallingPoints",
                column: "BusStopId");

            migrationBuilder.CreateIndex(
                name: "IX_BusCallingPoints_BusTimetableId",
                table: "BusCallingPoints",
                column: "BusTimetableId");

            migrationBuilder.CreateIndex(
                name: "IX_BusHolidays_BusTimetableId",
                table: "BusHolidays",
                column: "BusTimetableId");

            migrationBuilder.CreateIndex(
                name: "IX_BusHolidays_PublicHolidayName",
                table: "BusHolidays",
                column: "PublicHolidayName");

            migrationBuilder.CreateIndex(
                name: "IX_BusSpecialDays_BusTimetableId",
                table: "BusSpecialDays",
                column: "BusTimetableId");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_DatasetId",
                table: "BusTimetables",
                column: "DatasetId");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_LineName",
                table: "BusTimetables",
                column: "LineName");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_NationalOperatorRef",
                table: "BusTimetables",
                column: "NationalOperatorRef");

            migrationBuilder.CreateIndex(
                name: "IX_BusTimetables_TripScheduleKey",
                table: "BusTimetables",
                column: "TripScheduleKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusCallingPoints");

            migrationBuilder.DropTable(
                name: "BusHolidays");

            migrationBuilder.DropTable(
                name: "BusSpecialDays");

            migrationBuilder.DropTable(
                name: "PublicHolidays");

            migrationBuilder.DropTable(
                name: "BusTimetables");

            migrationBuilder.DropTable(
                name: "BusDatasets");
        }
    }
}

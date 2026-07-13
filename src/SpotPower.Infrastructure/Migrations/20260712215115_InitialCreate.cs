using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotPower.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DayAheadPrices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeliveryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Zone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PriceEurPerMWh = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    SourceFileName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayAheadPrices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DayAheadPrices_DeliveryDate",
                table: "DayAheadPrices",
                column: "DeliveryDate");

            migrationBuilder.CreateIndex(
                name: "IX_DayAheadPrices_PeriodStartUtc_Zone",
                table: "DayAheadPrices",
                columns: new[] { "PeriodStartUtc", "Zone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DayAheadPrices");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenefits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Benefits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoadmapItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    KpiId = table.Column<int>(type: "INTEGER", nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ExpectedValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    ExpectedBy = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Baseline = table.Column<decimal>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Benefits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Benefits_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Benefits_Items_RoadmapItemId",
                        column: x => x.RoadmapItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Benefits_ProcessKpis_KpiId",
                        column: x => x.KpiId,
                        principalTable: "ProcessKpis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BenefitMeasurements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BenefitId = table.Column<int>(type: "INTEGER", nullable: false),
                    MeasuredOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Value = table.Column<decimal>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenefitMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenefitMeasurements_Benefits_BenefitId",
                        column: x => x.BenefitId,
                        principalTable: "Benefits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BenefitMeasurements_BenefitId_MeasuredOn",
                table: "BenefitMeasurements",
                columns: new[] { "BenefitId", "MeasuredOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Benefits_ApplicationId",
                table: "Benefits",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Benefits_KpiId",
                table: "Benefits",
                column: "KpiId");

            migrationBuilder.CreateIndex(
                name: "IX_Benefits_RoadmapItemId",
                table: "Benefits",
                column: "RoadmapItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BenefitMeasurements");

            migrationBuilder.DropTable(
                name: "Benefits");
        }
    }
}

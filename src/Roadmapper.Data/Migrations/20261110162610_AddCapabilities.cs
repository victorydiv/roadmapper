using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CapabilityFeatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityFeatures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComparisonMembers",
                columns: table => new
                {
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonMembers", x => new { x.Category, x.ApplicationId });
                    table.ForeignKey(
                        name: "FK_ComparisonMembers_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeatureAssessments",
                columns: table => new
                {
                    FeatureId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Availability = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Strength = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureAssessments", x => new { x.FeatureId, x.ApplicationId });
                    table.ForeignKey(
                        name: "FK_FeatureAssessments_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FeatureAssessments_CapabilityFeatures_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "CapabilityFeatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityFeatures_Category_Name",
                table: "CapabilityFeatures",
                columns: new[] { "Category", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonMembers_ApplicationId",
                table: "ComparisonMembers",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureAssessments_ApplicationId",
                table: "FeatureAssessments",
                column: "ApplicationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComparisonMembers");

            migrationBuilder.DropTable(
                name: "FeatureAssessments");

            migrationBuilder.DropTable(
                name: "CapabilityFeatures");
        }
    }
}

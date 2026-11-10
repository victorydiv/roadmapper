using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCapabilityCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PricingLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Basis = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Minimum = table.Column<decimal>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AsOf = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingLines_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Users = table.Column<int>(type: "INTEGER", nullable: true),
                    Licenses = table.Column<int>(type: "INTEGER", nullable: true),
                    DockDoors = table.Column<int>(type: "INTEGER", nullable: true),
                    Assets = table.Column<int>(type: "INTEGER", nullable: true),
                    TransactionsPerMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PricingLines_ApplicationId",
                table: "PricingLines",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingLines_Category_ApplicationId",
                table: "PricingLines",
                columns: new[] { "Category", "ApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteProfiles_Category_Name",
                table: "SiteProfiles",
                columns: new[] { "Category", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PricingLines");

            migrationBuilder.DropTable(
                name: "SiteProfiles");
        }
    }
}

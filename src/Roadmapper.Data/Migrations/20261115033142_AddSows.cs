using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SowRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SowRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ProjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Site = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Manager = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PreparedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Overview = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    DetailedDescription = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Assumptions = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Schedule = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    IncludeCostDetail = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sows_TradingPartners_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "TradingPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SowSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpirationDays = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultManager = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Signatories = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CostFootnote = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Agreement = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    DefaultScope = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    DefaultAssumptions = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    DefaultSchedule = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SowSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SowLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SowId = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SowLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SowLines_Sows_SowId",
                        column: x => x.SowId,
                        principalTable: "Sows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SowRates",
                columns: new[] { "Id", "Category", "Frequency", "IsActive", "Name", "Note", "Rate", "SortOrder", "Unit" },
                values: new object[,]
                {
                    { 1, "LaborItPm", "OneTime", true, "Project management", "Project management and oversight", 150m, 0, "hour" },
                    { 2, "LaborItPm", "OneTime", true, "Product management", "Design review and as-needed support", 150m, 1, "hour" },
                    { 3, "LaborWms", "OneTime", true, "Design", null, 150m, 0, "hour" },
                    { 4, "LaborWms", "OneTime", true, "Development", null, 215m, 1, "hour" },
                    { 5, "LaborWms", "OneTime", true, "Configuration", null, 150m, 2, "hour" },
                    { 6, "LaborWms", "OneTime", true, "Testing", null, 150m, 3, "hour" },
                    { 7, "LaborWms", "OneTime", true, "Support", null, 150m, 4, "hour" },
                    { 8, "LaborWms", "OneTime", true, "BSA labor (non-bill)", "BSA labor for oversight, testing, and support.", 0m, 5, "hour" },
                    { 9, "LaborWms", "OneTime", true, "Vendor labor", "Don't change unless the vendor's rate is higher.", 150m, 6, "hour" },
                    { 10, "LaborWms", "OneTime", true, "Contingency", "Usually about 10% of the WMS hours, rounded up.", 150m, 7, "hour" },
                    { 11, "LaborEdi", "OneTime", true, "Design", "Kenco development hours", 80m, 0, "hour" },
                    { 12, "LaborEdi", "OneTime", true, "Development", "Cleo hours", 175m, 1, "hour" },
                    { 13, "LaborEdi", "OneTime", true, "Configuration", null, 150m, 2, "hour" },
                    { 14, "LaborEdi", "OneTime", true, "Testing", null, 150m, 3, "hour" },
                    { 15, "LaborEdi", "OneTime", true, "Support", null, 150m, 4, "hour" },
                    { 16, "LaborTms", "OneTime", true, "Design", null, 150m, 0, "hour" },
                    { 17, "LaborTms", "OneTime", true, "Development", null, 150m, 1, "hour" },
                    { 18, "LaborTms", "OneTime", true, "Configuration", null, 150m, 2, "hour" },
                    { 19, "LaborTms", "OneTime", true, "Testing", null, 150m, 3, "hour" },
                    { 20, "LaborTms", "OneTime", true, "Support", null, 150m, 4, "hour" },
                    { 21, "LaborTesting", "OneTime", true, "Application readiness", null, 150m, 0, "hour" },
                    { 22, "LaborTesting", "OneTime", true, "Application functional", null, 150m, 1, "hour" },
                    { 23, "LaborTesting", "OneTime", true, "Application regression", null, 150m, 2, "hour" },
                    { 24, "LaborTesting", "OneTime", true, "Application performance", null, 150m, 3, "hour" },
                    { 25, "LaborTesting", "OneTime", true, "Solution end-to-end", null, 150m, 4, "hour" },
                    { 26, "LaborTesting", "OneTime", true, "Solution integration", null, 150m, 5, "hour" },
                    { 27, "LaborTesting", "OneTime", true, "Solution regression", null, 150m, 6, "hour" },
                    { 28, "LaborTesting", "OneTime", true, "Interface failure", null, 150m, 7, "hour" },
                    { 29, "LaborTesting", "OneTime", true, "Interface performance", null, 150m, 8, "hour" },
                    { 30, "LaborTesting", "OneTime", true, "Installation qualification (IQ)", null, 150m, 9, "hour" },
                    { 31, "LaborTesting", "OneTime", true, "Operational qualification (OQ)", null, 150m, 10, "hour" },
                    { 32, "LaborTesting", "OneTime", true, "Performance qualification (PQ)", null, 150m, 11, "hour" },
                    { 33, "Software", "Annual", true, "WMS license", null, 1200m, 0, "each" },
                    { 34, "Software", "Annual", true, "LMS license", null, 300m, 1, "each" },
                    { 35, "Software", "Annual", true, "AS2 license maintenance", null, 500m, 2, "each" },
                    { 36, "Software", "Annual", true, "EDI annual support", "Enter the support amount as the quantity.", 1m, 3, "each" },
                    { 37, "Software", "Annual", true, "MercuryGate license", null, 0m, 4, "each" },
                    { 38, "Software", "OneTime", true, "AS2 license", null, 2500m, 5, "each" },
                    { 39, "Software", "OneTime", true, "MercuryGate license", null, 0m, 6, "each" },
                    { 40, "Software", "OneTime", true, "Software license", "For a Blue Yonder software license, add 9.25% sales tax to the cost.", 0m, 7, "each" },
                    { 41, "Travel", "OneTime", true, "Travel", "Per trip: airfare, hotel, car and meals.", 1800m, 0, "trip" }
                });

            migrationBuilder.InsertData(
                table: "SowSettings",
                columns: new[] { "Id", "Agreement", "CostFootnote", "DefaultAssumptions", "DefaultManager", "DefaultSchedule", "DefaultScope", "ExpirationDays", "Signatories" },
                values: new object[] { 1, "This Statement of Work describes the services Kenco shall supply, perform, and deliver in accordance with this Statement of Work and the Master Agreement. This Statement of Work is executed by a duly authorized representative of the parties as of the date set forth below.", "Total costs above do not include any charges from Kenco Logistics Services (KLS) such as management fees, G&A fees, or Cost of Working Capital charges.", "- Standard documents and labels will be used", null, "- Development to commence upon approval\n- QA schedule based on code completion\n- Go-live based upon testing completion\n- Once a firm schedule is determined, the customer will be notified", "- Project management\n- Configuration of the WMS\n- Unit testing, quality assurance testing\n- Production deployment\n- Go-live support", 90, "Kenco Management Services, LLC\nKenco Logistics Services, LLC" });

            migrationBuilder.CreateIndex(
                name: "IX_SowLines_SowId",
                table: "SowLines",
                column: "SowId");

            migrationBuilder.CreateIndex(
                name: "IX_Sows_CustomerId",
                table: "Sows",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sows_Status",
                table: "Sows",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SowLines");

            migrationBuilder.DropTable(
                name: "SowRates");

            migrationBuilder.DropTable(
                name: "SowSettings");

            migrationBuilder.DropTable(
                name: "Sows");
        }
    }
}

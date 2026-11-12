using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDevPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_OneTarget",
                table: "Documents");

            migrationBuilder.AddColumn<int>(
                name: "DevProjectId",
                table: "Documents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DevCostCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false, collation: "NOCASE"),
                    DefaultRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevCostCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DevProjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: true),
                    RoadmapItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CompletedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DevProjects_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DevProjects_Items_RoadmapItemId",
                        column: x => x.RoadmapItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DevProjects_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DevProjects_TradingPartners_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "TradingPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DevProjects_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DevCostLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DevProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Hours = table.Column<decimal>(type: "TEXT", nullable: true),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: true),
                    FixedAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevCostLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DevCostLines_DevCostCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "DevCostCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DevCostLines_DevProjects_DevProjectId",
                        column: x => x.DevProjectId,
                        principalTable: "DevProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "DevCostCategories",
                columns: new[] { "Id", "DefaultRate", "Name", "SortOrder" },
                values: new object[,]
                {
                    { 1, null, "DEV", 0 },
                    { 2, null, "QA", 1 },
                    { 3, null, "EDI", 2 },
                    { 4, null, "PM", 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DevProjectId",
                table: "Documents",
                column: "DevProjectId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_OneTarget",
                table: "Documents",
                sql: "(\"ApplicationId\" IS NOT NULL) + (\"VendorId\" IS NOT NULL) + (\"ContractId\" IS NOT NULL) + (\"RoadmapItemId\" IS NOT NULL) + (\"DevProjectId\" IS NOT NULL) <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_DevCostCategories_Name",
                table: "DevCostCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DevCostLines_CategoryId",
                table: "DevCostLines",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DevCostLines_DevProjectId",
                table: "DevCostLines",
                column: "DevProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DevProjects_ApplicationId",
                table: "DevProjects",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_DevProjects_CustomerId",
                table: "DevProjects",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DevProjects_ProductId",
                table: "DevProjects",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_DevProjects_RoadmapItemId",
                table: "DevProjects",
                column: "RoadmapItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DevProjects_VendorId",
                table: "DevProjects",
                column: "VendorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_DevProjects_DevProjectId",
                table: "Documents",
                column: "DevProjectId",
                principalTable: "DevProjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_DevProjects_DevProjectId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "DevCostLines");

            migrationBuilder.DropTable(
                name: "DevCostCategories");

            migrationBuilder.DropTable(
                name: "DevProjects");

            migrationBuilder.DropIndex(
                name: "IX_Documents_DevProjectId",
                table: "Documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_OneTarget",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "DevProjectId",
                table: "Documents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_OneTarget",
                table: "Documents",
                sql: "(\"ApplicationId\" IS NOT NULL) + (\"VendorId\" IS NOT NULL) + (\"ContractId\" IS NOT NULL) + (\"RoadmapItemId\" IS NOT NULL) <= 1");
        }
    }
}

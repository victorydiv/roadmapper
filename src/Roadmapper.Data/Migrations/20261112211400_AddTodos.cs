using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTodos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Todos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RemindOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CompletedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    RoadmapItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    DecisionId = table.Column<int>(type: "INTEGER", nullable: true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: true),
                    ContactId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Todos", x => x.Id);
                    table.CheckConstraint("CK_Todos_OneLink", "(\"RoadmapItemId\" IS NOT NULL) + (\"ApplicationId\" IS NOT NULL) + (\"DecisionId\" IS NOT NULL) + (\"VendorId\" IS NOT NULL) + (\"ContactId\" IS NOT NULL) <= 1");
                    table.ForeignKey(
                        name: "FK_Todos_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Todos_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Todos_Decisions_DecisionId",
                        column: x => x.DecisionId,
                        principalTable: "Decisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Todos_Items_RoadmapItemId",
                        column: x => x.RoadmapItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Todos_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Todos_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Todos_ApplicationId",
                table: "Todos",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Todos_CompletedOn_DueDate",
                table: "Todos",
                columns: new[] { "CompletedOn", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Todos_ContactId",
                table: "Todos",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Todos_DecisionId",
                table: "Todos",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Todos_ProductId",
                table: "Todos",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Todos_RoadmapItemId",
                table: "Todos",
                column: "RoadmapItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Todos_VendorId",
                table: "Todos",
                column: "VendorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Todos");
        }
    }
}

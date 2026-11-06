using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SourceTemplateAppliedAt",
                table: "Items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceTemplateName",
                table: "Items",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateBatchId",
                table: "Items",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskTemplates_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskTemplateLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TemplateId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    StartOffsetDays = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationDays = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTemplateLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskTemplateLines_TaskTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "TaskTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskTemplateLineTags",
                columns: table => new
                {
                    TagsId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskTemplateLineId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTemplateLineTags", x => new { x.TagsId, x.TaskTemplateLineId });
                    table.ForeignKey(
                        name: "FK_TaskTemplateLineTags_Tags_TagsId",
                        column: x => x.TagsId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskTemplateLineTags_TaskTemplateLines_TaskTemplateLineId",
                        column: x => x.TaskTemplateLineId,
                        principalTable: "TaskTemplateLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_TemplateBatchId",
                table: "Items",
                column: "TemplateBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplateLines_TemplateId_SortOrder",
                table: "TaskTemplateLines",
                columns: new[] { "TemplateId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplateLineTags_TaskTemplateLineId",
                table: "TaskTemplateLineTags",
                column: "TaskTemplateLineId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_ProductId_Name",
                table: "TaskTemplates",
                columns: new[] { "ProductId", "Name" },
                unique: true);

            // Give every existing product the starter template once. (New products get it from
            // RoadmapService.CreateProductAsync.) Literal values, so this migration never changes.
            migrationBuilder.Sql("""
                INSERT INTO TaskTemplates (ProductId, Name, Description, CreatedAt, UpdatedAt)
                SELECT Id, 'EDI Migration Standard', 'Standard task set for one customer EDI migration.',
                       strftime('%Y-%m-%d %H:%M:%S', 'now'), strftime('%Y-%m-%d %H:%M:%S', 'now')
                FROM Products;

                INSERT INTO TaskTemplateLines (TemplateId, SortOrder, Title)
                SELECT t.Id, l.SortOrder, l.Title
                FROM TaskTemplates t
                CROSS JOIN (SELECT 0 AS SortOrder, 'Build BRD' AS Title
                            UNION ALL SELECT 1, 'Review EDI documents'
                            UNION ALL SELECT 2, 'Develop EDI docs'
                            UNION ALL SELECT 3, 'Test EDI docs'
                            UNION ALL SELECT 4, 'TR/CAB'
                            UNION ALL SELECT 5, 'Implementation') l
                WHERE t.Name = 'EDI Migration Standard';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskTemplateLineTags");

            migrationBuilder.DropTable(
                name: "TaskTemplateLines");

            migrationBuilder.DropTable(
                name: "TaskTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Items_TemplateBatchId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SourceTemplateAppliedAt",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SourceTemplateName",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "TemplateBatchId",
                table: "Items");
        }
    }
}

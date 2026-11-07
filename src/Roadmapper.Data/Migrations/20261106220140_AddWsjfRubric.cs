using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWsjfRubric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WsjfScale",
                table: "Products",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManualFactors",
                table: "ItemScores",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Every WSJF value already entered was typed by hand, so a rubric added later must never overwrite it.
            migrationBuilder.Sql("""
                UPDATE ItemScores SET ManualFactors =
                    (CASE WHEN BusinessValue IS NOT NULL THEN 1 ELSE 0 END) +
                    (CASE WHEN TimeCriticality IS NOT NULL THEN 2 ELSE 0 END) +
                    (CASE WHEN RiskReduction IS NOT NULL THEN 4 ELSE 0 END) +
                    (CASE WHEN JobSize IS NOT NULL THEN 8 ELSE 0 END);
                """);

            migrationBuilder.CreateTable(
                name: "ScoringCriteria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Factor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Help = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoringCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoringCriteria_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScoringCriterionOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CriterionId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    From = table.Column<double>(type: "REAL", nullable: true),
                    Value = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoringCriterionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScoringCriterionOptions_ScoringCriteria_CriterionId",
                        column: x => x.CriterionId,
                        principalTable: "ScoringCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemRubricAnswers",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    CriterionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<double>(type: "REAL", nullable: true),
                    OptionId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemRubricAnswers", x => new { x.ItemId, x.CriterionId });
                    table.ForeignKey(
                        name: "FK_ItemRubricAnswers_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemRubricAnswers_ScoringCriteria_CriterionId",
                        column: x => x.CriterionId,
                        principalTable: "ScoringCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemRubricAnswers_ScoringCriterionOptions_OptionId",
                        column: x => x.OptionId,
                        principalTable: "ScoringCriterionOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemRubricAnswers_CriterionId",
                table: "ItemRubricAnswers",
                column: "CriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemRubricAnswers_OptionId",
                table: "ItemRubricAnswers",
                column: "OptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScoringCriteria_ProductId_SortOrder",
                table: "ScoringCriteria",
                columns: new[] { "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ScoringCriterionOptions_CriterionId",
                table: "ScoringCriterionOptions",
                column: "CriterionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemRubricAnswers");

            migrationBuilder.DropTable(
                name: "ScoringCriterionOptions");

            migrationBuilder.DropTable(
                name: "ScoringCriteria");

            migrationBuilder.DropColumn(
                name: "WsjfScale",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ManualFactors",
                table: "ItemScores");
        }
    }
}

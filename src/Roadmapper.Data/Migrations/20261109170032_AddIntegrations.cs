using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Integrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    SourceApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ViaApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MessageFormat = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BusinessPurpose = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Criticality = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastReviewDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReviewIntervalMonths = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 12),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Integrations", x => x.Id);
                    table.CheckConstraint("CK_Integrations_SourceNotTarget", "\"SourceApplicationId\" <> \"TargetApplicationId\"");
                    table.ForeignKey(
                        name: "FK_Integrations_Applications_SourceApplicationId",
                        column: x => x.SourceApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Integrations_Applications_TargetApplicationId",
                        column: x => x.TargetApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Integrations_Applications_ViaApplicationId",
                        column: x => x.ViaApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Integrations_Contacts_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationRisks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IntegrationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Likelihood = table.Column<int>(type: "INTEGER", nullable: false),
                    Impact = table.Column<int>(type: "INTEGER", nullable: false),
                    Mitigation = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OwnerId = table.Column<int>(type: "INTEGER", nullable: true),
                    RaisedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationRisks", x => x.Id);
                    table.CheckConstraint("CK_IntegrationRisks_Scale", "\"Likelihood\" BETWEEN 1 AND 5 AND \"Impact\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_IntegrationRisks_Contacts_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IntegrationRisks_Integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "Integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationRisks_IntegrationId",
                table: "IntegrationRisks",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationRisks_OwnerId",
                table: "IntegrationRisks",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_OwnerId",
                table: "Integrations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_SourceApplicationId",
                table: "Integrations",
                column: "SourceApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_TargetApplicationId",
                table: "Integrations",
                column: "TargetApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_ViaApplicationId",
                table: "Integrations",
                column: "ViaApplicationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrationRisks");

            migrationBuilder.DropTable(
                name: "Integrations");
        }
    }
}

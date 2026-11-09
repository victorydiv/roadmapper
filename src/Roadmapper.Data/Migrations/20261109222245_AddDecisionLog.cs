using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Decisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    DecidedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Context = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SupersededById = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Decisions_Decisions_SupersededById",
                        column: x => x.SupersededById,
                        principalTable: "Decisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DecisionAlternatives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DecisionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Option = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Pros = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Cons = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    WhyRejected = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionAlternatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DecisionAlternatives_Decisions_DecisionId",
                        column: x => x.DecisionId,
                        principalTable: "Decisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DecisionLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DecisionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    IntegrationId = table.Column<int>(type: "INTEGER", nullable: true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: true),
                    ContractId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: true),
                    RoadmapItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    TradingPartnerId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionLinks", x => x.Id);
                    table.CheckConstraint("CK_DecisionLinks_OneTarget", "(\"ApplicationId\" IS NOT NULL) + (\"IntegrationId\" IS NOT NULL) + (\"VendorId\" IS NOT NULL) + (\"ContractId\" IS NOT NULL) + (\"ProcessId\" IS NOT NULL) + (\"RoadmapItemId\" IS NOT NULL) + (\"TradingPartnerId\" IS NOT NULL) = 1");
                    table.ForeignKey(
                        name: "FK_DecisionLinks_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DecisionLinks_BusinessProcesses_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DecisionLinks_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DecisionLinks_Decisions_DecisionId",
                        column: x => x.DecisionId,
                        principalTable: "Decisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DecisionLinks_Integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "Integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DecisionLinks_Items_RoadmapItemId",
                        column: x => x.RoadmapItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DecisionLinks_TradingPartners_TradingPartnerId",
                        column: x => x.TradingPartnerId,
                        principalTable: "TradingPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DecisionLinks_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DecisionAlternatives_DecisionId",
                table: "DecisionAlternatives",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLinks_ApplicationId",
                table: "DecisionLinks",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLinks_ContractId",
                table: "DecisionLinks",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLinks_DecisionId",
                table: "DecisionLinks",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLinks_IntegrationId",
                table: "DecisionLinks",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLinks_ProcessId",
                table: "DecisionLinks",
                column: "ProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLinks_RoadmapItemId",
                table: "DecisionLinks",
                column: "RoadmapItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLinks_TradingPartnerId",
                table: "DecisionLinks",
                column: "TradingPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionLinks_VendorId",
                table: "DecisionLinks",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_DecidedOn",
                table: "Decisions",
                column: "DecidedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_SupersededById",
                table: "Decisions",
                column: "SupersededById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionAlternatives");

            migrationBuilder.DropTable(
                name: "DecisionLinks");

            migrationBuilder.DropTable(
                name: "Decisions");
        }
    }
}

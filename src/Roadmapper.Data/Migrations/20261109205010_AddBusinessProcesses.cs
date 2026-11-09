using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessProcesses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessProcesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ProcessOwnerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ParentProcessId = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessProcesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessProcesses_BusinessProcesses_ParentProcessId",
                        column: x => x.ParentProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessProcesses_Contacts_ProcessOwnerId",
                        column: x => x.ProcessOwnerId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProcessApplications",
                columns: table => new
                {
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Usage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessApplications", x => new { x.ProcessId, x.ApplicationId });
                    table.ForeignKey(
                        name: "FK_ProcessApplications_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessApplications_BusinessProcesses_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessIntegrations",
                columns: table => new
                {
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    IntegrationId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessIntegrations", x => new { x.ProcessId, x.IntegrationId });
                    table.ForeignKey(
                        name: "FK_ProcessIntegrations_BusinessProcesses_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessIntegrations_Integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "Integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessKpis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Baseline = table.Column<decimal>(type: "TEXT", nullable: true),
                    Target = table.Column<decimal>(type: "TEXT", nullable: true),
                    Current = table.Column<decimal>(type: "TEXT", nullable: true),
                    MeasuredOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessKpis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessKpis_BusinessProcesses_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessPainPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProcessId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    AddressedByItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessPainPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessPainPoints_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProcessPainPoints_BusinessProcesses_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessPainPoints_Items_AddressedByItemId",
                        column: x => x.AddressedByItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_Name",
                table: "BusinessProcesses",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_ParentProcessId",
                table: "BusinessProcesses",
                column: "ParentProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_ProcessOwnerId",
                table: "BusinessProcesses",
                column: "ProcessOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessApplications_ApplicationId",
                table: "ProcessApplications",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessIntegrations_IntegrationId",
                table: "ProcessIntegrations",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessKpis_ProcessId",
                table: "ProcessKpis",
                column: "ProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessPainPoints_AddressedByItemId",
                table: "ProcessPainPoints",
                column: "AddressedByItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessPainPoints_ApplicationId",
                table: "ProcessPainPoints",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessPainPoints_ProcessId",
                table: "ProcessPainPoints",
                column: "ProcessId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessApplications");

            migrationBuilder.DropTable(
                name: "ProcessIntegrations");

            migrationBuilder.DropTable(
                name: "ProcessKpis");

            migrationBuilder.DropTable(
                name: "ProcessPainPoints");

            migrationBuilder.DropTable(
                name: "BusinessProcesses");
        }
    }
}

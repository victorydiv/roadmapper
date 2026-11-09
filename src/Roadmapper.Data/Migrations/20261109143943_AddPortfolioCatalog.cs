using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioAuditEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    OldValue = table.Column<string>(type: "TEXT", nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    LaborRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    RenewalWarningDays = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultReviewIntervalMonths = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationInitiatives",
                columns: table => new
                {
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoadmapItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ImpactType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationInitiatives", x => new { x.ApplicationId, x.RoadmapItemId });
                    table.ForeignKey(
                        name: "FK_ApplicationInitiatives_Items_RoadmapItemId",
                        column: x => x.RoadmapItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: true),
                    ContractId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    BusinessOwnerId = table.Column<int>(type: "INTEGER", nullable: true),
                    TechnicalOwnerId = table.Column<int>(type: "INTEGER", nullable: true),
                    Criticality = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Hosting = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AnnualCost = table.Column<decimal>(type: "TEXT", nullable: true),
                    BusinessFit = table.Column<int>(type: "INTEGER", nullable: true),
                    TechnicalFit = table.Column<int>(type: "INTEGER", nullable: true),
                    GoLiveDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RetireDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationSupportContacts",
                columns: table => new
                {
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContactId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationSupportContacts", x => new { x.ApplicationId, x.ContactId, x.Role });
                    table.ForeignKey(
                        name: "FK_ApplicationSupportContacts_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: true),
                    Department = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AccountManagerId = table.Column<int>(type: "INTEGER", nullable: true),
                    TechnicalContactId = table.Column<int>(type: "INTEGER", nullable: true),
                    SupportPortalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SupportPhone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vendors_Contacts_AccountManagerId",
                        column: x => x.AccountManagerId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Vendors_Contacts_TechnicalContactId",
                        column: x => x.TechnicalContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContractNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RenewalDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    NoticePeriodDays = table.Column<int>(type: "INTEGER", nullable: true),
                    AutoRenews = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnnualValue = table.Column<decimal>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contracts_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorEscalationContacts",
                columns: table => new
                {
                    VendorId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContactId = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorEscalationContacts", x => new { x.VendorId, x.ContactId });
                    table.ForeignKey(
                        name: "FK_VendorEscalationContacts_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VendorEscalationContacts_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "PortfolioSettings",
                columns: new[] { "Id", "DefaultReviewIntervalMonths", "LaborRate", "RenewalWarningDays" },
                values: new object[] { 1, 12, null, 120 });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationInitiatives_RoadmapItemId",
                table: "ApplicationInitiatives",
                column: "RoadmapItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_BusinessOwnerId",
                table: "Applications",
                column: "BusinessOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ContractId",
                table: "Applications",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Criticality",
                table: "Applications",
                column: "Criticality");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Name",
                table: "Applications",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ProductId",
                table: "Applications",
                column: "ProductId",
                unique: true,
                filter: "\"ProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Status",
                table: "Applications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_TechnicalOwnerId",
                table: "Applications",
                column: "TechnicalOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_VendorId",
                table: "Applications",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationSupportContacts_ContactId",
                table: "ApplicationSupportContacts",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_Name",
                table: "Contacts",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_VendorId",
                table: "Contacts",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_RenewalDate",
                table: "Contracts",
                column: "RenewalDate");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_VendorId",
                table: "Contracts",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAuditEntries_ChangedAt",
                table: "PortfolioAuditEntries",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioAuditEntries_EntityType_EntityId",
                table: "PortfolioAuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorEscalationContacts_ContactId",
                table: "VendorEscalationContacts",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_AccountManagerId",
                table: "Vendors",
                column: "AccountManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_Name",
                table: "Vendors",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_TechnicalContactId",
                table: "Vendors",
                column: "TechnicalContactId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationInitiatives_Applications_ApplicationId",
                table: "ApplicationInitiatives",
                column: "ApplicationId",
                principalTable: "Applications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Contacts_BusinessOwnerId",
                table: "Applications",
                column: "BusinessOwnerId",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Contacts_TechnicalOwnerId",
                table: "Applications",
                column: "TechnicalOwnerId",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Contracts_ContractId",
                table: "Applications",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Vendors_VendorId",
                table: "Applications",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationSupportContacts_Contacts_ContactId",
                table: "ApplicationSupportContacts",
                column: "ContactId",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Contacts_Vendors_VendorId",
                table: "Contacts",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_Contacts_AccountManagerId",
                table: "Vendors");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_Contacts_TechnicalContactId",
                table: "Vendors");

            migrationBuilder.DropTable(
                name: "ApplicationInitiatives");

            migrationBuilder.DropTable(
                name: "ApplicationSupportContacts");

            migrationBuilder.DropTable(
                name: "PortfolioAuditEntries");

            migrationBuilder.DropTable(
                name: "PortfolioSettings");

            migrationBuilder.DropTable(
                name: "VendorEscalationContacts");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "Vendors");
        }
    }
}

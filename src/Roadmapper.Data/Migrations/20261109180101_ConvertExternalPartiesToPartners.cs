using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertExternalPartiesToPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kept apart from AddTradingPartners: SQLite rebuilds the Integrations table (to make its application columns
            // optional) at the end of that migration, so this has to run afterwards.
            // Customers, carriers and other outside companies used to be applications with the category ExternalParty.
            // Turn each into a trading partner, point its integrations at the partner, then remove the application.
            // Type is guessed from the name (carrier words → Carrier, else Customer); Tier 1 → Key; Retired → Inactive.
            migrationBuilder.Sql("""
                INSERT INTO "TradingPartners" ("Name", "Type", "Status", "Importance", "RelationshipOwnerId", "Notes", "CreatedAt", "UpdatedAt")
                SELECT "Name",
                       CASE WHEN lower("Name") LIKE '%carrier%' OR lower("Name") LIKE '%freight%' OR lower("Name") LIKE '%transport%'
                                 OR lower("Name") LIKE '%trucking%' OR lower("Name") LIKE '%parcel%' OR lower("Name") LIKE '%ltl%' THEN 'Carrier'
                            WHEN lower("Name") LIKE '%supplier%' THEN 'Supplier'
                            ELSE 'Customer' END,
                       CASE "Status" WHEN 'Retired' THEN 'Inactive' WHEN 'Planned' THEN 'Onboarding' ELSE 'Active' END,
                       CASE "Criticality" WHEN 'Tier1' THEN 'Key' ELSE 'Standard' END,
                       "BusinessOwnerId",
                       substr(NULLIF(trim(COALESCE("Description", '')
                           || CASE WHEN "Description" IS NOT NULL AND "Notes" IS NOT NULL THEN char(10) || char(10) ELSE '' END
                           || COALESCE("Notes", '')), ''), 1, 4000),
                       "CreatedAt", "UpdatedAt"
                FROM "Applications" WHERE "Category" = 'ExternalParty';

                UPDATE "Integrations"
                SET "SourcePartnerId" = (SELECT p."Id" FROM "TradingPartners" p JOIN "Applications" a ON a."Name" = p."Name"
                                         WHERE a."Id" = "Integrations"."SourceApplicationId"),
                    "SourceApplicationId" = NULL
                WHERE "SourceApplicationId" IN (SELECT "Id" FROM "Applications" WHERE "Category" = 'ExternalParty');

                UPDATE "Integrations"
                SET "TargetPartnerId" = (SELECT p."Id" FROM "TradingPartners" p JOIN "Applications" a ON a."Name" = p."Name"
                                         WHERE a."Id" = "Integrations"."TargetApplicationId"),
                    "TargetApplicationId" = NULL
                WHERE "TargetApplicationId" IN (SELECT "Id" FROM "Applications" WHERE "Category" = 'ExternalParty');

                UPDATE "Integrations" SET "ViaApplicationId" = NULL
                WHERE "ViaApplicationId" IN (SELECT "Id" FROM "Applications" WHERE "Category" = 'ExternalParty');

                INSERT INTO "PortfolioAuditEntries" ("EntityType", "EntityId", "EntityName", "Action", "Field", "OldValue", "NewValue", "ChangedAt")
                SELECT 'TradingPartner', "Id", "Name", 'Created', NULL, NULL, 'Converted from an External party application', strftime('%Y-%m-%d %H:%M:%f', 'now')
                FROM "TradingPartners";

                DELETE FROM "Applications" WHERE "Category" = 'ExternalParty';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // One-way: partners aren't turned back into applications.
        }
    }
}

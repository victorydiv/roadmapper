using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TargetApplicationId",
                table: "Integrations",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "SourceApplicationId",
                table: "Integrations",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "SourcePartnerId",
                table: "Integrations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetPartnerId",
                table: "Integrations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TradingPartnerId",
                table: "Contacts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TradingPartners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Importance = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AccountCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EdiQualifier = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    EdiId = table.Column<string>(type: "TEXT", maxLength: 35, nullable: true),
                    GsId = table.Column<string>(type: "TEXT", maxLength: 35, nullable: true),
                    RelationshipOwnerId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingPartners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradingPartners_Contacts_RelationshipOwnerId",
                        column: x => x.RelationshipOwnerId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_SourcePartnerId",
                table: "Integrations",
                column: "SourcePartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_TargetPartnerId",
                table: "Integrations",
                column: "TargetPartnerId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Integrations_OneSource",
                table: "Integrations",
                sql: "(\"SourceApplicationId\" IS NULL) <> (\"SourcePartnerId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Integrations_OneTarget",
                table: "Integrations",
                sql: "(\"TargetApplicationId\" IS NULL) <> (\"TargetPartnerId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_TradingPartnerId",
                table: "Contacts",
                column: "TradingPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingPartners_Name",
                table: "TradingPartners",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradingPartners_RelationshipOwnerId",
                table: "TradingPartners",
                column: "RelationshipOwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Contacts_TradingPartners_TradingPartnerId",
                table: "Contacts",
                column: "TradingPartnerId",
                principalTable: "TradingPartners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Integrations_TradingPartners_SourcePartnerId",
                table: "Integrations",
                column: "SourcePartnerId",
                principalTable: "TradingPartners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Integrations_TradingPartners_TargetPartnerId",
                table: "Integrations",
                column: "TargetPartnerId",
                principalTable: "TradingPartners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contacts_TradingPartners_TradingPartnerId",
                table: "Contacts");

            migrationBuilder.DropForeignKey(
                name: "FK_Integrations_TradingPartners_SourcePartnerId",
                table: "Integrations");

            migrationBuilder.DropForeignKey(
                name: "FK_Integrations_TradingPartners_TargetPartnerId",
                table: "Integrations");

            migrationBuilder.DropTable(
                name: "TradingPartners");

            migrationBuilder.DropIndex(
                name: "IX_Integrations_SourcePartnerId",
                table: "Integrations");

            migrationBuilder.DropIndex(
                name: "IX_Integrations_TargetPartnerId",
                table: "Integrations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Integrations_OneSource",
                table: "Integrations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Integrations_OneTarget",
                table: "Integrations");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_TradingPartnerId",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "SourcePartnerId",
                table: "Integrations");

            migrationBuilder.DropColumn(
                name: "TargetPartnerId",
                table: "Integrations");

            migrationBuilder.DropColumn(
                name: "TradingPartnerId",
                table: "Contacts");

            migrationBuilder.AlterColumn<int>(
                name: "TargetApplicationId",
                table: "Integrations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SourceApplicationId",
                table: "Integrations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}

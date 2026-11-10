using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class DocumentsPerProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_OneTarget",
                table: "Documents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DocumentFolders",
                table: "DocumentFolders");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "DocumentFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "DocumentFolders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_DocumentFolders",
                table: "DocumentFolders",
                column: "Id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_OneTarget",
                table: "Documents",
                sql: "(\"ApplicationId\" IS NOT NULL) + (\"VendorId\" IS NOT NULL) + (\"ContractId\" IS NOT NULL) + (\"RoadmapItemId\" IS NOT NULL) <= 1");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolders_Category",
                table: "DocumentFolders",
                column: "Category",
                unique: true,
                filter: "\"ProductId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolders_ProductId_Category",
                table: "DocumentFolders",
                columns: new[] { "ProductId", "Category" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentFolders_Products_ProductId",
                table: "DocumentFolders",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentFolders_Products_ProductId",
                table: "DocumentFolders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_OneTarget",
                table: "Documents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DocumentFolders",
                table: "DocumentFolders");

            migrationBuilder.DropIndex(
                name: "IX_DocumentFolders_Category",
                table: "DocumentFolders");

            migrationBuilder.DropIndex(
                name: "IX_DocumentFolders_ProductId_Category",
                table: "DocumentFolders");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "DocumentFolders");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "DocumentFolders");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DocumentFolders",
                table: "DocumentFolders",
                column: "Category");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_OneTarget",
                table: "Documents",
                sql: "(\"ProductId\" IS NOT NULL) + (\"ApplicationId\" IS NOT NULL) + (\"VendorId\" IS NOT NULL) + (\"ContractId\" IS NOT NULL) + (\"RoadmapItemId\" IS NOT NULL) <= 1");
        }
    }
}

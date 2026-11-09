using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Roadmapper.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemJiraLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JiraBaseUrl",
                table: "PortfolioSettings",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalUrl",
                table: "Items",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JiraKey",
                table: "Items",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "PortfolioSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "JiraBaseUrl",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JiraBaseUrl",
                table: "PortfolioSettings");

            migrationBuilder.DropColumn(
                name: "ExternalUrl",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "JiraKey",
                table: "Items");
        }
    }
}

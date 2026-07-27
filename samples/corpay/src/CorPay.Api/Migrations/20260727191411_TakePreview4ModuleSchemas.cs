using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorPay.Api.Migrations
{
    /// <inheritdoc />
    public partial class TakePreview4ModuleSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreErasureContentHash",
                table: "GoldpathArchiveEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreErasureContentHash",
                table: "GoldpathArchiveEntries");
        }
    }
}

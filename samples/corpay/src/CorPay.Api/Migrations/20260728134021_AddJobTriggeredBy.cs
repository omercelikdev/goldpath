using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorPay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddJobTriggeredBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TriggeredBy",
                table: "GoldpathJobRuns",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TriggeredBy",
                table: "GoldpathJobRuns");
        }
    }
}

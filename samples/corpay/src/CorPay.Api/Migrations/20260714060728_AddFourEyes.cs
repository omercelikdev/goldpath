using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorPay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFourEyes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "PaymentInstructions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "PaymentInstructions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "PaymentInstructions");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "PaymentInstructions");
        }
    }
}

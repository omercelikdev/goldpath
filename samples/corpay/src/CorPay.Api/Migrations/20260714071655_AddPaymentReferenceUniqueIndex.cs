using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorPay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReferenceUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PaymentInstructions_TenantId_Reference",
                table: "PaymentInstructions",
                columns: new[] { "TenantId", "Reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentInstructions_TenantId_Reference",
                table: "PaymentInstructions");
        }
    }
}

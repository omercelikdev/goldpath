using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CorPay.EodWorker.Migrations
{
    /// <inheritdoc />
    public partial class AddEodReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EodReconciliationRow",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Executed = table.Column<int>(type: "integer", nullable: false),
                    Matched = table.Column<int>(type: "integer", nullable: false),
                    Mismatched = table.Column<int>(type: "integer", nullable: false),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EodReconciliationRow", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EodReconciliationRow_Day_TenantId",
                table: "EodReconciliationRow",
                columns: new[] { "Day", "TenantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EodReconciliationRow");
        }
    }
}

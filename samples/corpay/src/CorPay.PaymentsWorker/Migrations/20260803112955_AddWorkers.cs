using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CorPay.PaymentsWorker.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcessedWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedWorkItems", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedWorkItems");
        }
    }
}

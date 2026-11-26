using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embrulhado.Fulfillment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlacementCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "placement_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    corrected_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    previous_supplier_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    previous_paid_product_cost_cents = table.Column<long>(type: "bigint", nullable: true),
                    previous_paid_freight_cost_cents = table.Column<long>(type: "bigint", nullable: true),
                    supplier_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    paid_product_cost_cents = table.Column<long>(type: "bigint", nullable: true),
                    paid_freight_cost_cents = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_placement_corrections", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_placement_corrections_order_id",
                table: "placement_corrections",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "placement_corrections");
        }
    }
}

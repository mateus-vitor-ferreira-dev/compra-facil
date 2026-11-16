using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Fulfillment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManualOrderActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "paid_freight_cost_cents",
                table: "supplier_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "paid_product_cost_cents",
                table: "supplier_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "placed_by",
                table: "supplier_orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paid_freight_cost_cents",
                table: "supplier_orders");

            migrationBuilder.DropColumn(
                name: "paid_product_cost_cents",
                table: "supplier_orders");

            migrationBuilder.DropColumn(
                name: "placed_by",
                table: "supplier_orders");
        }
    }
}

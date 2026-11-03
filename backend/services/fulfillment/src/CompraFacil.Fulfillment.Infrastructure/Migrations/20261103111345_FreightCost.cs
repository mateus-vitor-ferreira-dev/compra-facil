using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Fulfillment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FreightCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "freight_cost_cents",
                table: "supplier_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_service",
                table: "supplier_orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "freight_cost_cents",
                table: "supplier_orders");

            migrationBuilder.DropColumn(
                name: "shipping_service",
                table: "supplier_orders");
        }
    }
}

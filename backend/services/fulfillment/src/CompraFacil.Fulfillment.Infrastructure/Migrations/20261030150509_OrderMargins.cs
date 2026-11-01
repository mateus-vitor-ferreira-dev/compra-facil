using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Fulfillment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderMargins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "freight_cents",
                table: "supplier_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "product_cost_cents",
                table: "supplier_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "revenue_cents",
                table: "supplier_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_payment_fees",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_cents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_payment_fees", x => x.order_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_payment_fees");

            migrationBuilder.DropColumn(
                name: "freight_cents",
                table: "supplier_orders");

            migrationBuilder.DropColumn(
                name: "product_cost_cents",
                table: "supplier_orders");

            migrationBuilder.DropColumn(
                name: "revenue_cents",
                table: "supplier_orders");
        }
    }
}

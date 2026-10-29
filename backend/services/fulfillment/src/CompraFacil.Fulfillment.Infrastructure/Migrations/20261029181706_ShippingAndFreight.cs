using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Fulfillment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShippingAndFreight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "promised_by",
                table: "supplier_orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "freight_cents",
                table: "invoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "promised_by",
                table: "supplier_orders");

            migrationBuilder.DropColumn(
                name: "freight_cents",
                table: "invoices");
        }
    }
}

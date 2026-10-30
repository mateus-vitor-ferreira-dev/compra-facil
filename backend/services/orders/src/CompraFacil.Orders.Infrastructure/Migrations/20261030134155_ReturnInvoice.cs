using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReturnInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "return_invoice_access_key",
                table: "orders",
                type: "character varying(44)",
                maxLength: 44,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "return_invoice_issued_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "return_invoice_number",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "return_invoice_series",
                table: "orders",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "return_invoice_access_key",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "return_invoice_issued_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "return_invoice_number",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "return_invoice_series",
                table: "orders");
        }
    }
}

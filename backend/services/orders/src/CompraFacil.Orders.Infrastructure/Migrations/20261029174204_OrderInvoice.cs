using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "invoice_access_key",
                table: "orders",
                type: "character varying(44)",
                maxLength: 44,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "invoice_cancelled_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "invoice_issued_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "invoice_number",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "invoice_series",
                table: "orders",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "invoice_access_key",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "invoice_cancelled_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "invoice_issued_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "invoice_number",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "invoice_series",
                table: "orders");
        }
    }
}

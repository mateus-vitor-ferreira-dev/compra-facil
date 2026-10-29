using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderSupplierSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivered_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "refunded_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sent_to_supplier_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "shipped_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tracking_number",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tracking_provider",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tracking_url",
                table: "orders",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivered_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "refunded_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "sent_to_supplier_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipped_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tracking_number",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tracking_provider",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tracking_url",
                table: "orders");
        }
    }
}

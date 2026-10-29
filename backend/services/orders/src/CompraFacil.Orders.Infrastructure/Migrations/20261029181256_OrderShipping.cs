using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "shipping_max_days",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "shipping_min_days",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_name",
                table: "orders",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "shipping_price_cents",
                table: "orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_price_currency",
                table: "orders",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "shipping_promised_by",
                table: "orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shipping_service",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shipping_max_days",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_min_days",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_name",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_price_cents",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_price_currency",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_promised_by",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "shipping_service",
                table: "orders");
        }
    }
}

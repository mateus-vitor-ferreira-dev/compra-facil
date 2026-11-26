using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embrulhado.Fulfillment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManualSupplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "awaiting_staff_since",
                table: "supplier_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "manual_catalog_items",
                columns: table => new
                {
                    sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cost_cents = table.Column<long>(type: "bigint", nullable: false),
                    stock = table.Column<int>(type: "integer", nullable: false),
                    weight_grams = table.Column<int>(type: "integer", nullable: true),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_catalog_items", x => x.sku);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manual_catalog_items");

            migrationBuilder.DropColumn(
                name: "awaiting_staff_since",
                table: "supplier_orders");
        }
    }
}

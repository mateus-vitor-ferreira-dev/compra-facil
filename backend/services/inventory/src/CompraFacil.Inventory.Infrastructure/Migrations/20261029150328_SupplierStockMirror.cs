using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SupplierStockMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "supplier",
                table: "stock_items",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_vid",
                table: "stock_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supplier_stock",
                columns: table => new
                {
                    supplier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    vid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    available = table.Column<int>(type: "integer", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_supplier_stock", x => new { x.supplier, x.vid });
                    table.CheckConstraint("ck_supplier_stock_non_negative", "available >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_supplier_supplier_vid",
                table: "stock_items",
                columns: new[] { "supplier", "supplier_vid" },
                filter: "supplier_vid IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_stock");

            migrationBuilder.DropIndex(
                name: "ix_stock_items_supplier_supplier_vid",
                table: "stock_items");

            migrationBuilder.DropColumn(
                name: "supplier",
                table: "stock_items");

            migrationBuilder.DropColumn(
                name: "supplier_vid",
                table: "stock_items");
        }
    }
}

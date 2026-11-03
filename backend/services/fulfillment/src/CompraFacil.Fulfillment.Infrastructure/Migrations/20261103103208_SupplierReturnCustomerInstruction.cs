using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Fulfillment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SupplierReturnCustomerInstruction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customer_instruction",
                table: "supplier_returns",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "instructed_at",
                table: "supplier_returns",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "customer_instruction",
                table: "supplier_returns");

            migrationBuilder.DropColumn(
                name: "instructed_at",
                table: "supplier_returns");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "return_requested_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "return_requested_at",
                table: "orders");
        }
    }
}

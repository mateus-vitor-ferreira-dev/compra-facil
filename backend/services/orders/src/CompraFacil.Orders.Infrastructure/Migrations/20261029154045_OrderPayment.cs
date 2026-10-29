using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "installments",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                table: "orders",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Card"); // os pedidos de antes foram cobrados direto (gateway falso), como cartão

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pix_expires_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_qr_code",
                table: "orders",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_qr_code_base64",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_ticket_url",
                table: "orders",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "installments",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_method",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pix_expires_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pix_qr_code",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pix_qr_code_base64",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "pix_ticket_url",
                table: "orders");
        }
    }
}

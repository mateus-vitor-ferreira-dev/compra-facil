using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PaymentProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "captured_at",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "card_last_four",
                table: "payments",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "fee_cents",
                table: "payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "installments",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "method",
                table: "payments",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Card"); // os de antes (gateway falso) eram cobranças diretas, como cartão

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pix_expires_at",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_qr_code",
                table: "payments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_qr_code_base64",
                table: "payments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pix_ticket_url",
                table: "payments",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "refunded_cents",
                table: "payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "refunds",
                table: "payments",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "payments",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "card_brand",
                table: "charges",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "card_token",
                table: "charges",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_email",
                table: "charges",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "installments",
                table: "charges",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "method",
                table: "charges",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Pix");

            migrationBuilder.CreateIndex(
                name: "ix_payments_provider_provider_reference",
                table: "payments",
                columns: new[] { "provider", "provider_reference" },
                unique: true,
                filter: "provider_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_provider_provider_reference",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "captured_at",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "card_last_four",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "fee_cents",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "installments",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "method",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "pix_expires_at",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "pix_qr_code",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "pix_qr_code_base64",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "pix_ticket_url",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refunded_cents",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refunds",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "card_brand",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "card_token",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "customer_email",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "installments",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "method",
                table: "charges");
        }
    }
}

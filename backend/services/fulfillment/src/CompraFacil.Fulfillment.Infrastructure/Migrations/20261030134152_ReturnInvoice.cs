using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Fulfillment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReturnInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "return_access_key",
                table: "invoices",
                type: "character varying(44)",
                maxLength: 44,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "return_claimed_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_error",
                table: "invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "return_issued_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "return_number",
                table: "invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_provider_id",
                table: "invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "return_requested_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "return_series",
                table: "invoices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_return_requested_at",
                table: "invoices",
                column: "return_requested_at",
                filter: "return_requested_at IS NOT NULL AND return_access_key IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoices_return_requested_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "return_access_key",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "return_claimed_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "return_error",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "return_issued_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "return_number",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "return_provider_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "return_requested_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "return_series",
                table: "invoices");
        }
    }
}

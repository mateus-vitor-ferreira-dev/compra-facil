using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EfiProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_reversal",
                table: "payments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_city",
                table: "charges",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_complement",
                table: "charges",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_district",
                table: "charges",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_number",
                table: "charges",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_phone",
                table: "charges",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_state",
                table: "charges",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_street",
                table: "charges",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_zip_code",
                table: "charges",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_status",
                table: "payments",
                column: "status",
                filter: "status = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_status",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "pending_reversal",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "payer_city",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "payer_complement",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "payer_district",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "payer_number",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "payer_phone",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "payer_state",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "payer_street",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "payer_zip_code",
                table: "charges");
        }
    }
}

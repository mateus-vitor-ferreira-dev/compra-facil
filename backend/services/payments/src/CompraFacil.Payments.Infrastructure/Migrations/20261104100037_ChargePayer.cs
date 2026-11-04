using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Payments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChargePayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payer_cpf",
                table: "charges",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payer_name",
                table: "charges",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payer_cpf",
                table: "charges");

            migrationBuilder.DropColumn(
                name: "payer_name",
                table: "charges");
        }
    }
}

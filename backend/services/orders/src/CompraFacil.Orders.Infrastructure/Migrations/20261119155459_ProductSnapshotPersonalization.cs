using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Orders.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductSnapshotPersonalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "personalization_max_length",
                table: "product_snapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "personalization_required",
                table: "product_snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "personalization_max_length",
                table: "product_snapshots");

            migrationBuilder.DropColumn(
                name: "personalization_required",
                table: "product_snapshots");
        }
    }
}

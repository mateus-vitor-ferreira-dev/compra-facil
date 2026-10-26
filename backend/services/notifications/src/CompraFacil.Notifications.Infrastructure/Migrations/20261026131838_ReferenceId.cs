using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReferenceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "order_id",
                table: "notifications",
                newName: "reference_id");

            migrationBuilder.RenameIndex(
                name: "ix_notifications_order_id",
                table: "notifications",
                newName: "ix_notifications_reference_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "reference_id",
                table: "notifications",
                newName: "order_id");

            migrationBuilder.RenameIndex(
                name: "ix_notifications_reference_id",
                table: "notifications",
                newName: "ix_notifications_order_id");
        }
    }
}

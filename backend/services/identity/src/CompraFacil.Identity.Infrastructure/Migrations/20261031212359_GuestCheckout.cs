using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GuestCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "guest_checkout_key",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guest_email",
                table: "users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_guest_email",
                table: "users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "guest_access_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guest_access_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_guest_access_links_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_guest_checkout_key",
                table: "users",
                column: "guest_checkout_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_guest_email",
                table: "users",
                column: "normalized_guest_email");

            migrationBuilder.CreateIndex(
                name: "ix_guest_access_links_token_hash",
                table: "guest_access_links",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guest_access_links_user_id_created_at",
                table: "guest_access_links",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guest_access_links");

            migrationBuilder.DropIndex(
                name: "ix_users_guest_checkout_key",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_normalized_guest_email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "guest_checkout_key",
                table: "users");

            migrationBuilder.DropColumn(
                name: "guest_email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "normalized_guest_email",
                table: "users");
        }
    }
}

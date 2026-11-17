using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraFacil.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokenSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "refresh_tokens",
                type: "text",
                nullable: false,
                defaultValue: "");

            // as sessões de antes desta versão ganham o carimbo atual da conta e continuam abertas; o
            // que a última troca de senha já revogou continua revogado
            migrationBuilder.Sql(
                """
                UPDATE refresh_tokens AS t
                SET security_stamp = COALESCE(u.security_stamp, '')
                FROM users AS u
                WHERE u.id = t.user_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "refresh_tokens");
        }
    }
}

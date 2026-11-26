using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embrulhado.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// A loja trocou de nome (CompraFácil → Embrulhado), e dois valores com o nome antigo estavam gravados:
    /// <list type="bullet">
    /// <item>o último passo do TOTP usado (anti-reuso do código) fica em <c>user_tokens</c> com o provedor
    /// <c>[CompraFacilTotp]</c>, agora <c>[EmbrulhadoTotp]</c>;</item>
    /// <item>as chaves privadas de <c>signing_keys</c> foram cifradas pelo Data Protection com o nome de aplicação
    /// <c>compra-facil-identity</c>. Com <c>embrulhado-identity</c> elas não decifram mais, então saem: na subida
    /// seguinte o anel cria uma chave nova (lista vazia é a primeira subida). Os access tokens em uso deixam de
    /// valer e o front renova pelo refresh token, que fica no banco e não depende dessas chaves.</item>
    /// </list>
    /// </summary>
    public partial class RenomeiaParaEmbrulhado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE user_tokens SET login_provider = '[EmbrulhadoTotp]' WHERE login_provider = '[CompraFacilTotp]';
                DELETE FROM signing_keys;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // as chaves apagadas não voltam; o código anterior cria outra ao subir, como aqui
            migrationBuilder.Sql(
                """
                UPDATE user_tokens SET login_provider = '[CompraFacilTotp]' WHERE login_provider = '[EmbrulhadoTotp]';
                DELETE FROM signing_keys;
                """);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Embrulhado.Fulfillment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SupplierOrderItemSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // o tamanho entra no JSON dos itens (coluna items, jsonb): sem DDL; os itens de antes ficam sem tamanho
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

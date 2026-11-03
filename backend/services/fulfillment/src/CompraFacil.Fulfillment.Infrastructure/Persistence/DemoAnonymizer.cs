using CompraFacil.BuildingBlocks.Data;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Fulfillment.Infrastructure.Persistence;

/// <summary>
/// Retenção da demonstração (#280): mascara o destino (ship_to, em jsonb) do pedido ao fornecedor e da nota fiscal
/// antigos, como o orders: nome, CPF, telefone, rua, número, complemento e bairro; o CEP perde os três últimos
/// dígitos. A nota emitida no simulador fica só na memória dele, que zera a cada deploy.
/// </summary>
internal sealed class FulfillmentDemoAnonymizer(FulfillmentDbContext db) : IPersonalDataAnonymizer
{
    private const string MaskedCpf = "00000000000";

    public async Task<int> AnonymizeAsync(DateTimeOffset createdBefore, CancellationToken ct) =>
        await MaskAsync("supplier_orders", createdBefore, ct) + await MaskAsync("invoices", createdBefore, ct);

    private Task<int> MaskAsync(string table, DateTimeOffset createdBefore, CancellationToken ct) =>
        // a tabela vem da lista acima, nunca de fora: só os valores vão como parâmetro
#pragma warning disable EF1002
        db.Database.ExecuteSqlRawAsync($$"""
            UPDATE {{table}} SET ship_to = ship_to || jsonb_build_object(
                'Name', 'Cliente da demonstração', 'Cpf', @p1, 'Phone', @p1, 'Street', 'Removido', 'Number', '0',
                'Complement', NULL, 'District', 'Removido', 'ZipCode', left(ship_to->>'ZipCode', 5) || '000')
            WHERE created_at < @p0 AND ship_to IS NOT NULL AND ship_to->>'Cpf' <> @p1
            """, [new Npgsql.NpgsqlParameter("p0", createdBefore), new Npgsql.NpgsqlParameter("p1", MaskedCpf)], ct);
#pragma warning restore EF1002
}

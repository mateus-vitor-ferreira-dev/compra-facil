using CompraFacil.BuildingBlocks.Data;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Orders.Infrastructure.Persistence;

/// <summary>
/// Retenção da demonstração (#280): mascara quem recebe (nome, CPF, telefone) e a rua do pedido antigo. Ficam a
/// cidade, a UF e o CEP sem os três últimos dígitos, que não identificam ninguém e mantêm o frete e o histórico.
/// O CPF zerado marca o que já foi mascarado.
/// </summary>
internal sealed class OrdersDemoAnonymizer(OrdersDbContext db) : IPersonalDataAnonymizer
{
    public const string MaskedCpf = "00000000000";

    public Task<int> AnonymizeAsync(DateTimeOffset createdBefore, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE orders SET
                recipient_name = 'Cliente da demonstração', recipient_cpf = {MaskedCpf}, recipient_phone = {MaskedCpf},
                shipping_street = 'Removido', shipping_number = '0', shipping_complement = NULL, shipping_district = 'Removido',
                shipping_zip_code = left(shipping_zip_code, 5) || '000'
            WHERE created_at < {createdBefore} AND recipient_cpf IS NOT NULL AND recipient_cpf <> {MaskedCpf}
            """, ct);
}

using CompraFacil.Payments.Domain;

namespace CompraFacil.Payments.Application.Payments;

/// <summary>
/// Taxas do provedor por meio de pagamento (seção Payments:Fees), em porcentagem do valor, para a margem
/// real do pedido. Padrões da tabela pública da Efí (sejaefi.com.br/tarifas, 2026-11), sem antecipação: PIX
/// 1,19%; crédito à vista 3,49%, de 2 a 6 parcelas 3,99% e de 7 a 12, 4,39%. O contrato da conta pode ter outras.
/// </summary>
public sealed class FeeOptions
{
    public const string Section = "Payments:Fees";

    public decimal PixPercent { get; set; } = 1.19m;

    /// <summary>Por número de parcelas; parcelas sem entrada usam a maior faixa abaixo delas.</summary>
    public IDictionary<int, decimal> CardPercentByInstallments { get; } = new Dictionary<int, decimal>
    {
        [1] = 3.49m,
        [2] = 3.99m,
        [7] = 4.39m,
    };

    public long FeeCents(PaymentMethod method, int installments, long amountCents)
    {
        var percent = method == PaymentMethod.Pix
            ? PixPercent
            : CardPercentByInstallments.Where(kv => kv.Key <= installments).OrderByDescending(kv => kv.Key)
                .Select(kv => kv.Value).DefaultIfEmpty(0m).First();
        return (long)Math.Ceiling(amountCents * percent / 100m);
    }
}

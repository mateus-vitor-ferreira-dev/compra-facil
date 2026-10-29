using CompraFacil.Payments.Domain;

namespace CompraFacil.Payments.Application.Payments;

/// <summary>
/// Taxas do provedor por meio de pagamento (seção Payments:Fees), em porcentagem do valor, para a margem
/// real do pedido. Padrões da pesquisa do ADR 0007 (Mercado Pago, 2026): PIX 0,99%; cartão à vista 4,98%
/// e parcelado subindo até 13,6% em 12x.
/// </summary>
public sealed class FeeOptions
{
    public const string Section = "Payments:Fees";

    public decimal PixPercent { get; set; } = 0.99m;

    /// <summary>Por número de parcelas; parcelas sem entrada usam a maior faixa abaixo delas.</summary>
    public IDictionary<int, decimal> CardPercentByInstallments { get; } = new Dictionary<int, decimal>
    {
        [1] = 4.98m,
        [2] = 9.9m,
        [6] = 12.9m,
        [12] = 13.6m,
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

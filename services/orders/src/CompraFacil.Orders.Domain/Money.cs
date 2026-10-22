namespace CompraFacil.Orders.Domain;

/// <summary>Valor em centavos, como em todos os contratos (evita erro de ponto flutuante).</summary>
public readonly record struct Money(long AmountCents, string Currency)
{
    public const string DefaultCurrency = "BRL";

    public static Money Zero(string currency = DefaultCurrency) => new(0, currency);

    public Money Times(int quantity) => this with { AmountCents = checked(AmountCents * quantity) };

    public Money Plus(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new DomainException($"moedas diferentes: {Currency} e {other.Currency}");
        }
        return this with { AmountCents = checked(AmountCents + other.AmountCents) };
    }
}

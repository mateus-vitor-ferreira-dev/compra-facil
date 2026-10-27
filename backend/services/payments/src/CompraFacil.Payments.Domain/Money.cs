namespace CompraFacil.Payments.Domain;

/// <summary>Valor em centavos, como em todos os contratos.</summary>
public readonly record struct Money(long AmountCents, string Currency);

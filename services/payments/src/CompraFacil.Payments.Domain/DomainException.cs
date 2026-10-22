namespace CompraFacil.Payments.Domain;

/// <summary>Regra de pagamento violada (ex.: aprovar um pagamento já recusado).</summary>
public sealed class DomainException(string message) : Exception(message);

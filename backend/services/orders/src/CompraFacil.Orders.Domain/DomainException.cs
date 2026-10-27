namespace CompraFacil.Orders.Domain;

/// <summary>Regra de negócio violada (ex.: transição de estado inválida).</summary>
public sealed class DomainException(string message) : Exception(message);

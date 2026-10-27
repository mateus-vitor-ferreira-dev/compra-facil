namespace CompraFacil.Inventory.Domain;

/// <summary>Regra de estoque violada (ex.: baixa abaixo do reservado).</summary>
public sealed class DomainException(string message) : Exception(message);

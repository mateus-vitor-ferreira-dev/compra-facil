namespace CompraFacil.Inventory.Domain;

/// <summary>
/// Quantas unidades uma variante tem no fornecedor (#218), como o fulfillment leu por último. Fica à parte
/// do saldo do produto: o SupplierStockChanged e o ProductCreated chegam em tópicos diferentes, em qualquer
/// ordem, e o espelho se monta com os dois.
/// </summary>
public sealed class SupplierStockLevel
{
    private SupplierStockLevel()
    {
        // EF Core
    }

    public string Supplier { get; private set; } = "";
    public string Vid { get; private set; } = "";
    public int Available { get; private set; }
    public DateTimeOffset CheckedAt { get; private set; }

    public static SupplierStockLevel Create(string supplier, string vid, int available, DateTimeOffset checkedAt) =>
        new() { Supplier = supplier, Vid = vid, Available = Math.Max(available, 0), CheckedAt = checkedAt };

    /// <summary>Uma leitura nova; a mais velha que a guardada (evento atrasado) não vale. True se aplicou.</summary>
    public bool Apply(int available, DateTimeOffset checkedAt)
    {
        if (checkedAt < CheckedAt)
        {
            return false;
        }
        Available = Math.Max(available, 0);
        CheckedAt = checkedAt;
        return true;
    }
}

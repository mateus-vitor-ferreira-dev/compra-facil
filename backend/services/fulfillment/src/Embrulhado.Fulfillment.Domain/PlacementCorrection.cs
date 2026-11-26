namespace Embrulhado.Fulfillment.Domain;

/// <summary>
/// Fornecedor manual (#480): a equipe corrigiu o que tinha registrado ao fazer o pedido no fornecedor (o número dele, o
/// que pagou pelos produtos e pelo frete). Cada correção fica guardada com o antes e o depois, quem e quando: a margem
/// passa a usar os valores novos, e o histórico mostra de onde vieram.
/// </summary>
public sealed class PlacementCorrection
{
    private PlacementCorrection()
    {
        // EF Core
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public DateTimeOffset CorrectedAt { get; private set; }
    public string CorrectedBy { get; private set; } = "";

    public string? PreviousSupplierOrderId { get; private set; }
    public long? PreviousPaidProductCostCents { get; private set; }
    public long? PreviousPaidFreightCostCents { get; private set; }

    public string SupplierOrderId { get; private set; } = "";
    public long? PaidProductCostCents { get; private set; }
    public long? PaidFreightCostCents { get; private set; }

    internal static PlacementCorrection Of(SupplierOrder before, string supplierOrderId, long? paidProductCostCents, long? paidFreightCostCents,
        string correctedBy, DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(now),
            OrderId = before.Id,
            CorrectedAt = now,
            CorrectedBy = correctedBy,
            PreviousSupplierOrderId = before.SupplierOrderId,
            PreviousPaidProductCostCents = before.PaidProductCostCents,
            PreviousPaidFreightCostCents = before.PaidFreightCostCents,
            SupplierOrderId = supplierOrderId,
            PaidProductCostCents = paidProductCostCents,
            PaidFreightCostCents = paidFreightCostCents,
        };
}

using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.ManualSupplier;

/// <summary>A última planilha do fornecedor manual (#412), que o adaptador lê como o feed dele.</summary>
public interface IManualCatalogRepository
{
    Task<List<ManualCatalogItem>> ListAsync(CancellationToken ct);

    /// <summary>Troca a planilha inteira pela nova, numa transação.</summary>
    Task ReplaceAsync(IReadOnlyList<ManualCatalogItem> items, CancellationToken ct);

    Task<DateTimeOffset?> LastImportAtAsync(CancellationToken ct);
}

/// <summary>O resultado da importação: os erros por linha (nada mudou) ou o que a sincronização fez na loja.</summary>
public sealed record ManualImportResult(int Products, IReadOnlyList<SheetError> Errors, CatalogSyncResult? Sync);

/// <summary>O modo do fornecedor não é o manual: não há planilha para subir.</summary>
public sealed class ManualSupplierDisabledException() : Exception("o fornecedor não está no modo manual (Supplier:Mode)");

/// <summary>
/// Sobe a planilha do fornecedor manual (#412): lê e confere tudo antes, grava a nova no lugar da anterior e roda a
/// mesma sincronização do feed (<see cref="CatalogSync"/>): o que é novo, mudou ou saiu da planilha chega à loja
/// pelos eventos de sempre. Uma linha com erro e nada muda.
/// </summary>
public sealed class ManualCatalogImport(IManualCatalogRepository sheet, CatalogSync sync, ManualSupplierMode mode, TimeProvider clock)
{
    /// <exception cref="ManualSupplierDisabledException">O fornecedor configurado não é o manual.</exception>
    /// <exception cref="CatalogSyncBusyException">Outra sincronização está rodando; a planilha já foi gravada e entra na próxima.</exception>
    public async Task<ManualImportResult> ImportAsync(byte[] csv, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(csv);
        if (!mode.Enabled)
        {
            throw new ManualSupplierDisabledException();
        }
        if (csv.Length > ManualCatalogSheet.MaxBytes)
        {
            return new ManualImportResult(0, [new SheetError(1, $"o arquivo passa de {ManualCatalogSheet.MaxBytes / 1024 / 1024} MB")], null);
        }
        var parsed = ManualCatalogSheet.Parse(ManualCatalogSheet.Decode(csv), clock.GetUtcNow());
        if (!parsed.Ok)
        {
            return new ManualImportResult(0, parsed.Errors, null);
        }
        await sheet.ReplaceAsync(parsed.Items, ct);
        return new ManualImportResult(parsed.Items.Count, [], await sync.RunAsync(ct));
    }
}

/// <summary>Se o fornecedor é o manual (Supplier:Mode = manual).</summary>
public sealed record ManualSupplierMode(bool Enabled)
{
    /// <summary>O nome do fornecedor manual nos pedidos, nos eventos e no catálogo.</summary>
    public const string SupplierName = "manual";
}

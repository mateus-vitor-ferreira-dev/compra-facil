using Embrulhado.BuildingBlocks.Messaging;
using Embrulhado.Fulfillment.Domain;
using Embrulhado.Fulfillment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Embrulhado.Fulfillment.Infrastructure;

/// <summary>Banco do fulfillment (database-per-service). Tabelas e colunas em snake_case.</summary>
public sealed class FulfillmentDbContext(DbContextOptions<FulfillmentDbContext> options) : DbContext(options)
{
    public DbSet<SupplierOrder> SupplierOrders => Set<SupplierOrder>();
    public DbSet<SupplierProduct> SupplierProducts => Set<SupplierProduct>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<SupplierReturn> SupplierReturns => Set<SupplierReturn>();
    public DbSet<OrderPaymentFee> OrderPaymentFees => Set<OrderPaymentFee>();
    public DbSet<PlacementCorrection> PlacementCorrections => Set<PlacementCorrection>();
    public DbSet<ManualCatalogItem> ManualCatalogItems => Set<ManualCatalogItem>();
    internal DbSet<SupplierWebhook> SupplierWebhooks => Set<SupplierWebhook>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder
        .AddMessagingTables() // outbox e inbox (BuildingBlocks)
        .ApplyConfigurationsFromAssembly(typeof(FulfillmentDbContext).Assembly);
}

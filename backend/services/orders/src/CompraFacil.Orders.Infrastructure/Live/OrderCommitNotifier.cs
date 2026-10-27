using System.Data.Common;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CompraFacil.Orders.Infrastructure.Live;

/// <summary>
/// Avisa os streams abertos nesta réplica assim que a mudança do pedido é confirmada no banco, sem
/// esperar o evento dar a volta pelo outbox e pelo Kafka (o OrderChangeListener). É o que faz aparecer
/// a etapa da cobrança, que dura menos que o ciclo do relay. Uma instância por DbContext: guarda os
/// pedidos alterados no SaveChanges e avisa no commit (ou logo, sem transação explícita).
/// </summary>
internal sealed class OrderCommitNotifier(OrderChangeFeed feed) : DbTransactionInterceptor, ISaveChangesInterceptor
{
    private readonly HashSet<Guid> _changed = [];

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyIfCommitted(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        NotifyIfCommitted(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => _changed.Clear();

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _changed.Clear();
        return Task.CompletedTask;
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Notify();

    public override Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Notify();
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _changed.Clear();

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _changed.Clear();
        return Task.CompletedTask;
    }

    // só pedidos que já existiam: um pedido novo ainda não tem quem o acompanhe
    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }
        foreach (var entry in context.ChangeTracker.Entries<Order>())
        {
            if (entry.State == EntityState.Modified)
            {
                _changed.Add(entry.Entity.Id);
            }
        }
    }

    // dentro de uma transação explícita (o consumer de eventos), o aviso espera o commit
    private void NotifyIfCommitted(DbContext? context)
    {
        if (context?.Database.CurrentTransaction is null)
        {
            Notify();
        }
    }

    private void Notify()
    {
        foreach (var id in _changed)
        {
            feed.Notify(id);
        }
        _changed.Clear();
    }
}

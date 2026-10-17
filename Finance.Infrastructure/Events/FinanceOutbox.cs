using System.Data.Common;
using Finance.Application.Events;
using Hestia.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Wolverine.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore.Internals;
using Wolverine.Runtime;

namespace Finance.Infrastructure.Events;

/// <summary>
/// The transactional outbox of the finance events (T04, D7), with Wolverine. For each save of the
/// <see cref="Persistence.FinanceDbContext"/>:
/// <list type="number">
/// <item>Before the save, the raised events become Wolverine envelopes for the durable local queue
/// <see cref="FinanceEventsSetup.ForwardQueue"/> (T79), added to the same save as rows of
/// <c>wolverine.wolverine_incoming_envelopes</c>. So the change and its events commit together, or neither does.</item>
/// <item>After the commit (of the save, or of the explicit transaction around it), Wolverine runs
/// <see cref="FinanceEventForwarder{TEvent}"/>, which sends each event to RabbitMQ. If the broker is down or the process
/// stops, the rows stay, and Wolverine tries again later.</item>
/// <item>A failed save stores no envelope: its events go back to the queue, for a later save. A rolled-back
/// transaction sends nothing, nor does one that failed and was disposed before a retry.</item>
/// </list>
/// One instance per request (scoped).
/// </summary>
public sealed class FinanceOutbox(FinanceEventCollector collector, IWolverineRuntime runtime, ILogger<FinanceOutbox> logger)
{
    private DbContextOutbox? _inSave;
    private IReadOnlyList<IIntegrationEvent> _inSaveEvents = [];
    private readonly List<DbContextOutbox> _waitingForCommit = [];

    internal async Task SavingAsync(DbContext? context)
    {
        if (context is null)
        {
            return;
        }
        var events = collector.TakePending();
        if (events.Count == 0)
        {
            return;
        }

        // One outbox per save, so a failed save leaves nothing behind for the next one.
        var outbox = new DbContextOutbox(runtime, []);
        outbox.Enroll(context);
        _inSave = outbox;
        _inSaveEvents = events;
        foreach (var @event in events)
        {
            // The route is a durable local queue: Wolverine adds the envelope to the context (an IncomingMessage row).
            await outbox.PublishAsync(@event);
        }
    }

    internal async Task SavedAsync(DbContext? context)
    {
        if (_inSave is not { } outbox || context is null)
        {
            return;
        }
        _inSave = null;
        _inSaveEvents = [];
        if (context.Database.CurrentTransaction is null)
        {
            await FlushAsync(outbox);
        }
        else
        {
            _waitingForCommit.Add(outbox);
        }
    }

    internal void Failed(DbContext? context)
    {
        if (_inSave is null)
        {
            return;
        }
        if (context is not null)
        {
            foreach (var entry in context.ChangeTracker.Entries()
                         .Where(e => e is { Entity: IncomingMessage or OutgoingMessage, State: EntityState.Added }).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
        collector.Return(_inSaveEvents);
        _inSave = null;
        _inSaveEvents = [];
    }

    internal async Task CommittedAsync()
    {
        var outboxes = _waitingForCommit.ToList();
        _waitingForCommit.Clear();
        foreach (var outbox in outboxes)
        {
            await FlushAsync(outbox);
        }
    }

    internal void RolledBack() => _waitingForCommit.Clear();

    // A transaction that failed after a save, then was disposed (not rolled back), leaves its outboxes here: drop them,
    // or the commit of the retry sends that attempt's events too.
    internal void Started() => _waitingForCommit.Clear();

    private async Task FlushAsync(DbContextOutbox outbox)
    {
        try
        {
            await outbox.FlushOutgoingMessagesAsync();
        }
        catch (Exception ex)
        {
            // The change and its envelopes are committed: Wolverine's durability agent runs them later.
            logger.LogError(ex, "The finance events are in the outbox, but the send failed. Wolverine sends them later.");
        }
    }
}

/// <summary>The save half of the outbox: runs <see cref="FinanceOutbox"/> around each SaveChanges.</summary>
public sealed class FinanceOutboxSaveInterceptor(FinanceOutbox outbox) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        outbox.SavingAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await outbox.SavingAsync(eventData.Context);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        outbox.SavedAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        await outbox.SavedAsync(eventData.Context);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => outbox.Failed(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        outbox.Failed(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => outbox.Failed(eventData.Context);

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        outbox.Failed(eventData.Context);
        return Task.CompletedTask;
    }
}

/// <summary>The transaction half of the outbox: sends after a commit, drops after a rollback or at a new start.</summary>
public sealed class FinanceOutboxTransactionInterceptor(FinanceOutbox outbox) : DbTransactionInterceptor
{
    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        outbox.Started();
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
        DbTransaction result, CancellationToken cancellationToken = default)
    {
        outbox.Started();
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        outbox.CommittedAsync().GetAwaiter().GetResult();

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) => outbox.CommittedAsync();

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => outbox.RolledBack();

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        outbox.RolledBack();
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => outbox.RolledBack();

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        outbox.RolledBack();
        return Task.CompletedTask;
    }
}

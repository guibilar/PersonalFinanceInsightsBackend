using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Finance.Application.Events;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Events;
using Finance.Infrastructure.Persistence;
using Hestia.Contracts.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FinanceTests;

/// <summary>
/// With the finance events on, a change is one database transaction (T04), which the retrying execution strategy runs
/// again after a transient failure (a dropped connection, a deadlock). The new attempt starts from the database, not
/// from what the failed one left in the change tracker.
/// </summary>
public class RetryTests
{
    [Fact]
    public async Task DeletingATransaction_WhoseFirstCommitFails_IsStillDeleted()
    {
        Assert.SkipUnless(TestDatabase.OnPostgres, "Needs PostgreSQL: set FINANCE_TEST_POSTGRES (e.g. to \"container\").");
        var failCommit = new FailNextCommit();
        await using var factory = Factory(failCommit);
        var client = factory.CreateClient();
        var id = Guid.NewGuid();
        await factory.SeedAsync(db => db.Transactions.Add(new Transaction
        {
            Id = id, Date = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), Description = "BOOKSTORE", Amount = 30,
            TransactionType = TransactionType.Debit, AccountId = Account.DefaultCreditCardId, Hash = Guid.NewGuid().ToString()
        }));
        failCommit.Armed = true;

        var response = await client.DeleteAsync($"/api/Transaction/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(failCommit.Armed);
        Assert.True(await factory.QueryAsync(db =>
            db.Transactions.IgnoreQueryFilters().Where(t => t.Id == id).Select(t => t.Hidden).SingleAsync()));
    }

    [Fact]
    public async Task ImportingAStatement_WhoseFirstCommitFails_StoresItOnce()
    {
        Assert.SkipUnless(TestDatabase.OnPostgres, "Needs PostgreSQL: set FINANCE_TEST_POSTGRES (e.g. to \"container\").");
        var failCommit = new FailNextCommit();
        await using var factory = Factory(failCommit);
        var client = factory.CreateClient();
        failCommit.Armed = true;

        var response = await client.PostAsync("/api/Transaction/Import",
            ImportTests.Statement("2026-01-03,TIM HORTONS,2.15,\n2026-01-04,METRO,45.10,"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(failCommit.Armed);
        Assert.Equal(2, (await response.ReadAsync<ImportResultDto>()).Added);
        Assert.Equal(2, await factory.QueryAsync(db => db.Transactions.CountAsync()));
        Assert.NotNull(await factory.QueryAsync(db =>
            db.Accounts.Where(a => a.Id == Account.DefaultCreditCardId).Select(a => a.LastImportedAt).SingleAsync()));
    }

    [Fact]
    public async Task AddingATransactionAndABudget_WhoseFirstCommitFails_StoresEachOnce()
    {
        Assert.SkipUnless(TestDatabase.OnPostgres, "Needs PostgreSQL: set FINANCE_TEST_POSTGRES (e.g. to \"container\").");
        var failCommit = new FailNextCommit();
        await using var factory = Factory(failCommit);
        var client = factory.CreateClient();

        failCommit.Armed = true;
        var transaction = await client.PostAsJsonAsync("/api/Transaction", new
        {
            date = "2026-09-10", description = "FARMERS MARKET", amount = 12.5m, transactionType = "Debit",
            accountId = Account.DefaultCreditCardId, categoryId = DefaultCategories.Groceries
        });
        failCommit.Armed = true;
        var budget = await client.PostAsJsonAsync("/api/Budget", new
        {
            name = "Food", description = "Groceries", status = "Active", amount = 100, period = "Monthly",
            categoryIds = new[] { DefaultCategories.Groceries }
        });

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (transaction.StatusCode, budget.StatusCode));
        Assert.False(failCommit.Armed);
        Assert.Equal(1, await factory.QueryAsync(db => db.Transactions.CountAsync()));
        Assert.Equal(1, await factory.QueryAsync(db => db.Budgets.Include(b => b.Categories).Where(b => b.Categories.Count == 1).CountAsync()));
    }

    [Fact]
    public async Task AnUpdate_WhoseFirstAttemptFailsAfterItsSave_ForwardsItsEventOnce()
    {
        Assert.SkipUnless(TestDatabase.OnPostgres, "Needs PostgreSQL: set FINANCE_TEST_POSTGRES (e.g. to \"container\").");
        var failQuery = new FailNextQueryAfterASave();
        var forwards = new ForwardLog();
        // The outbox and its forward queue, with a broker that is down: enough to see what the commit hands to the queue.
        await using var gate = new BrokerGate("amqp://guest:guest@localhost:5672");
        FinanceApiFactory? factory = null;
        factory = new FinanceApiFactory(services =>
        {
            services.AddFinanceEvents(new FinanceEventsOptions
            {
                RabbitMq = gate.Address, Broker = new FinanceBrokerOptions { StartTimeout = TimeSpan.Zero }
            }, factory!.ConnectionString!);
            services.ConfigureDbContext<FinanceDbContext>(options => options.AddInterceptors(failQuery));
            services.AddSingleton<ILoggerProvider>(forwards);
            services.AddLogging(logging => logging.AddFilter<ForwardLog>("Wolverine", LogLevel.Debug));
        });
        await using var _ = factory;
        var client = factory.CreateClient();
        var id = Guid.NewGuid();
        await factory.SeedAsync(db => db.Transactions.Add(new Transaction
        {
            Id = id, Date = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc), Description = "BOOKSTORE", Amount = 30,
            TransactionType = TransactionType.Debit, AccountId = Account.DefaultCreditCardId, Hash = Guid.NewGuid().ToString()
        }));

        // The update saves the transaction and its event, then the budget check after it fails: the strategy retries.
        failQuery.Armed = true;
        var response = await client.PutAsJsonAsync($"/api/Transaction/{id}", new { description = "Books", categoryId = DefaultCategories.Shopping });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(failQuery.Armed);
        Assert.Single(forwards.Enqueued(nameof(TransactionChanged)));
    }

    /// <summary>The events on, without a broker: FinanceChanges runs each change in a transaction.</summary>
    private static FinanceApiFactory Factory(FailNextCommit failCommit) => new(services =>
    {
        services.Replace(ServiceDescriptor.Singleton(new FinanceEventsSwitch(Enabled: true)));
        services.ConfigureDbContext<FinanceDbContext>(options => options.AddInterceptors(failCommit));
    });

    /// <summary>The events that the commits hand to the forward queue, from the Wolverine log.</summary>
    private sealed class ForwardLog : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Enqueued(string eventName) =>
            _lines.Where(line => line.Contains($"Enqueued for sending {eventName}#", StringComparison.Ordinal)).ToList();

        public ILogger CreateLogger(string categoryName) => new Logger(_lines);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => lines.Enqueue(formatter(state, exception));
        }
    }

    /// <summary>When armed, fails the first query after a save once, as a dropped connection would.</summary>
    private sealed class FailNextQueryAfterASave : SaveChangesInterceptor, IDbCommandInterceptor
    {
        public volatile bool Armed;
        private volatile bool _saved;

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            _saved = Armed;
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (_saved && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
            {
                Armed = false;
                _saved = false;
                throw new NpgsqlException("The connection dropped.", new TimeoutException());
            }
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Fails the next commit once, as a dropped connection would: a transient error that the strategy retries.</summary>
    private sealed class FailNextCommit : DbTransactionInterceptor
    {
        public volatile bool Armed;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                throw new NpgsqlException("The connection dropped.", new TimeoutException());
            }
            return ValueTask.FromResult(result);
        }
    }
}

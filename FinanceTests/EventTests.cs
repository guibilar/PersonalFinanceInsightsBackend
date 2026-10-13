using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Finance.Application.Abstractions;
using Finance.Application.Events;
using Finance.Application.Models;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Events;
using Finance.Infrastructure.Persistence;
using Finance.Infrastructure.Repositories;
using Hestia.Contracts.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine.EntityFrameworkCore.Internals;
using Wolverine.Runtime;

namespace FinanceTests;

/// <summary>
/// The finance events (T04, D7): each change writes its event to the Wolverine outbox in the same transaction, and
/// Wolverine sends it to the RabbitMQ exchange after the commit. A rolled-back change sends nothing.
/// These tests need PostgreSQL and RabbitMQ (FINANCE_TEST_POSTGRES and FINANCE_TEST_RABBITMQ; CI uses "container").
/// </summary>
public class EventTests
{
    private static readonly TimeZoneInfo ReportingZone = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

    /// <summary>Today in the reporting time zone, as the budgets count it.</summary>
    private static DateTime Today => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ReportingZone).Date;

    private static string Day(DateTime date) => date.ToString("yyyy-MM-dd");

    private const string CountOutgoingEnvelopes =
        "SELECT count(*)::int AS \"Value\" FROM " + FinanceEventsOptions.OutboxSchema + ".wolverine_outgoing_envelopes";

    [Fact]
    public async Task AddingATransaction_SendsTransactionChanged()
    {
        await using var host = await StartAsync();
        var description = $"FARMERS MARKET {Guid.NewGuid():N}";

        var created = await (await host.Client.PostAsJsonAsync("/api/Transaction", new
        {
            date = "2026-09-10", description, amount = 12.5m, transactionType = "Debit",
            accountId = Account.DefaultCreditCardId, categoryId = DefaultCategories.Groceries
        }, FinanceApiFactory.Json)).ReadAsync<TransactionOutputDto>();

        var sent = await host.Events.WaitForAsync<TransactionChanged>(e => e.TransactionId == created.Id);
        Assert.Equal(TransactionChange.Added, sent.Change);
        Assert.Equal(Account.DefaultCreditCardId, sent.AccountId);
        Assert.Equal(new DateOnly(2026, 9, 10), sent.Date);
        Assert.Equal(description, sent.Description);
        Assert.Equal(-12.5m, sent.Amount);
        Assert.Equal(DefaultCategories.Groceries, sent.CategoryId);
        Assert.Equal("Groceries", sent.CategoryName);
        Assert.Equal(2, sent.Version);
        Assert.NotEqual(Guid.Empty, sent.EventId);
    }

    [Fact]
    public async Task UpdatingAndDeletingATransaction_SendTransactionChanged()
    {
        await using var host = await StartAsync();
        var id = await AddAsync(host.Client, $"BOOKSTORE {Guid.NewGuid():N}", 30m, Today.AddMonths(-2));

        await host.Client.PutAsJsonAsync($"/api/Transaction/{id}",
            new { description = "Books for school", categoryId = DefaultCategories.Shopping }, FinanceApiFactory.Json);
        await host.Client.DeleteAsync($"/api/Transaction/{id}");

        var updated = await host.Events.WaitForAsync<TransactionChanged>(e => e.TransactionId == id && e.Change == TransactionChange.Updated);
        Assert.Equal("Books for school", updated.Description);
        Assert.Equal("Shopping", updated.CategoryName);
        var deleted = await host.Events.WaitForAsync<TransactionChanged>(e => e.TransactionId == id && e.Change == TransactionChange.Deleted);
        // Each event has its own id (Hestia T36: the consumers handle each id once).
        Assert.NotEqual(updated.EventId, deleted.EventId);
    }

    [Fact]
    public async Task ImportingAStatement_SendsTransactionsImported()
    {
        await using var host = await StartAsync();
        var marker = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var csv = $"""
            2026-01-03,TIM HORTONS {marker},2.15,
            2026-01-04,METRO {marker},45.10,
            """;
        await host.Client.PostAsync("/api/Transaction/Import", ImportTests.Statement(csv));

        var again = await host.Client.PostAsync("/api/Transaction/Import", ImportTests.Statement(csv));

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var first = await host.Events.WaitForAsync<TransactionsImported>(e => e.Added == 2 && e.FirstDate == new DateOnly(2026, 1, 3));
        Assert.Equal(Account.DefaultCreditCardId, first.AccountId);
        Assert.Equal((2, 2, 0), (first.Read, first.Added, first.Skipped));
        Assert.Equal(new DateOnly(2026, 1, 4), first.LastDate);
        // The import that added nothing is an import too.
        await host.Events.WaitForAsync<TransactionsImported>(e => e.Added == 0 && e.Skipped == 2 && e.FirstDate == new DateOnly(2026, 1, 3));
    }

    [Fact]
    public async Task EnteringABalance_SendsBalanceRecorded()
    {
        await using var host = await StartAsync();
        var amount = 1000m + Random.Shared.Next(1, 100000) / 100m;

        var response = await host.Client.PostAsJsonAsync($"/api/Accounts/{Account.DefaultSavingsId}/Balances",
            new { date = Day(Today.AddDays(-1)), amount }, FinanceApiFactory.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = await host.Events.WaitForAsync<BalanceRecorded>(e => e.Amount == amount);
        Assert.Equal(Account.DefaultSavingsId, sent.AccountId);
        Assert.Equal(DateOnly.FromDateTime(Today.AddDays(-1)), sent.Date);
        Assert.Equal(BalanceSource.Entered, sent.Source);
    }

    [Fact]
    public async Task SpendingThatCrossesABudgetThreshold_SendsBudgetLimitReached_OncePerCrossing()
    {
        await using var host = await StartAsync();
        var budget = await (await host.Client.PostAsJsonAsync("/api/Budget", new
        {
            name = $"Groceries {Guid.NewGuid():N}", description = "d", status = "Active", amount = 100, period = "Monthly",
            categoryIds = new[] { DefaultCategories.Groceries }
        }, FinanceApiFactory.Json)).ReadAsync<BudgetOutputDto>();

        await AddAsync(host.Client, "GROCER A", 50m, Today);    // 50 %: nothing
        await AddAsync(host.Client, "GROCER B", 35m, Today);    // 85 %: the warning
        await AddAsync(host.Client, "GROCER C", 5m, Today);     // 90 %: nothing new
        await AddAsync(host.Client, "GROCER D", 20m, Today);    // 110 %: the limit

        var warning = await host.Events.WaitForAsync<BudgetLimitReached>(e => e.BudgetId == budget.Id && e.Percent == 80);
        Assert.Equal(100m, warning.Limit);
        Assert.Equal(85m, warning.Spent);
        Assert.Equal(new DateOnly(Today.Year, Today.Month, 1), warning.PeriodStart);
        var limit = await host.Events.WaitForAsync<BudgetLimitReached>(e => e.BudgetId == budget.Id && e.Percent == 100);
        Assert.Equal(110m, limit.Spent);
        Assert.Equal(2, (await host.Events.AfterQuietPeriodAsync<BudgetLimitReached>(e => e.BudgetId == budget.Id)).Count);
    }

    [Fact]
    public async Task ANewSubscriptionPrice_SendsSubscriptionPriceChanged()
    {
        await using var host = await StartAsync();
        var merchant = $"STREAMFLIX{Random.Shared.Next(100000, 999999)}";
        for (var monthsAgo = 4; monthsAgo >= 1; monthsAgo--)
        {
            await AddAsync(host.Client, merchant, 16.49m, Today.AddMonths(-monthsAgo), DefaultCategories.Subscriptions);
        }

        await AddAsync(host.Client, merchant, 18.99m, Today, DefaultCategories.Subscriptions);

        var sent = await host.Events.WaitForAsync<SubscriptionPriceChanged>(e => e.Merchant.Contains(merchant, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(16.49m, sent.OldPrice);
        Assert.Equal(18.99m, sent.NewPrice);
        Assert.Equal(DateOnly.FromDateTime(Today), sent.Since);
        Assert.Equal("Monthly", sent.Cadence);
        // Only the charge at the new price sends it.
        Assert.Single(await host.Events.AfterQuietPeriodAsync<SubscriptionPriceChanged>(e => e.Merchant.Contains(merchant, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task TheEvent_IsInTheSameSave_AsTheChange()
    {
        var saves = new SaveRecorder();
        await using var host = await StartAsync(services =>
            services.ConfigureDbContext<FinanceDbContext>(options => options.AddInterceptors(saves)));
        var id = await AddAsync(host.Client, $"HARDWARE {Guid.NewGuid():N}", 18m, Today.AddMonths(-3));

        await host.Client.PutAsJsonAsync($"/api/Transaction/{id}",
            new { description = "Paint", categoryId = DefaultCategories.Shopping }, FinanceApiFactory.Json);

        // One save holds the changed transaction and the envelope of its event: they commit together.
        Assert.Contains(saves.Saves, save => save.Contains($"Modified {nameof(Transaction)}") && save.Contains($"Added {nameof(OutgoingMessage)}"));
        await host.Events.WaitForAsync<TransactionChanged>(e => e.TransactionId == id && e.Description == "Paint");
    }

    [Fact]
    public async Task AChangeThatRollsBack_SendsNothing_AndLeavesNoEnvelope()
    {
        var failure = new FailAfterTheChange();
        await using var host = await StartAsync(services =>
        {
            services.AddSingleton(failure);
            services.AddScoped<BudgetRepository>();
            services.Replace(ServiceDescriptor.Scoped<IBudgetRepository>(provider =>
                new FailingBudgetRepository(provider.GetRequiredService<BudgetRepository>(), failure)));
        });
        var id = await AddAsync(host.Client, $"PHARMACY {Guid.NewGuid():N}", 22m, Today.AddMonths(-3));

        // The update saves the transaction and its event, then fails before the commit.
        failure.On = true;
        var response = await host.Client.PutAsJsonAsync($"/api/Transaction/{id}",
            new { description = "Never stored", categoryId = DefaultCategories.Health }, FinanceApiFactory.Json);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEqual("Never stored", await host.Factory.QueryAsync(db => db.Transactions.Where(t => t.Id == id).Select(t => t.Description).SingleAsync()));
        Assert.Empty(await host.Events.AfterQuietPeriodAsync<TransactionChanged>(e => e.TransactionId == id && e.Change == TransactionChange.Updated));
        Assert.Equal(0, await host.Factory.QueryAsync(db => db.Database.SqlQueryRaw<int>(CountOutgoingEnvelopes).SingleAsync()));
    }

    [Fact]
    public async Task WithoutTheSetting_FinanceHasNoEvents()
    {
        await using var factory = new FinanceApiFactory();

        Assert.False(factory.Services.GetRequiredService<FinanceEventsSwitch>().Enabled);
        Assert.Null(factory.Services.GetService<IWolverineRuntime>());
        Assert.Null(factory.Services.GetService<FinanceOutbox>());
    }

    // ---- Helpers

    private sealed class EventsHost(FinanceApiFactory factory, HttpClient client, FinanceEventQueue events) : IAsyncDisposable
    {
        public FinanceApiFactory Factory { get; } = factory;
        public HttpClient Client { get; } = client;
        public FinanceEventQueue Events { get; } = events;

        public async ValueTask DisposeAsync()
        {
            await Events.DisposeAsync();
            await Factory.DisposeAsync();
        }
    }

    /// <summary>The finance API with the events on (as Events:RabbitMq does in Program), and a queue on the exchange.</summary>
    private static async Task<EventsHost> StartAsync(Action<IServiceCollection>? more = null)
    {
        Assert.SkipUnless(TestDatabase.OnPostgres, $"Needs PostgreSQL: set {TestDatabase.Variable}.");
        Assert.SkipUnless(TestRabbitMq.Available, $"Needs RabbitMQ: set {TestRabbitMq.Variable}.");
        FinanceApiFactory? factory = null;
        factory = new FinanceApiFactory(services =>
        {
            services.AddFinanceEvents(new FinanceEventsOptions { RabbitMq = TestRabbitMq.Address }, factory!.ConnectionString!);
            more?.Invoke(services);
        });
        var client = factory.CreateClient();
        await factory.Services.EnsureFinanceOutboxAsync();
        var events = await FinanceEventQueue.BindAsync();
        return new EventsHost(factory, client, events);
    }

    private static async Task<Guid> AddAsync(HttpClient client, string description, decimal amount, DateTime date, Guid? categoryId = null)
    {
        var response = await client.PostAsJsonAsync("/api/Transaction", new
        {
            date = Day(date), description, amount, transactionType = "Debit",
            accountId = Account.DefaultCreditCardId, categoryId = categoryId ?? DefaultCategories.Groceries
        }, FinanceApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<TransactionOutputDto>()).Id;
    }

    /// <summary>Writes down the tracked entities of each save, as "State Type" lines.</summary>
    private sealed class SaveRecorder : SaveChangesInterceptor
    {
        public ConcurrentQueue<IReadOnlyList<string>> Saves { get; } = new();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves.Enqueue(eventData.Context!.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(e => $"{e.State} {e.Entity.GetType().Name}")
                .ToList());
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailAfterTheChange
    {
        public volatile bool On;
    }

    /// <summary>When <see cref="FailAfterTheChange.On"/>: the second budget check of a request (after the change) fails.</summary>
    private sealed class FailingBudgetRepository(BudgetRepository inner, FailAfterTheChange failure) : IBudgetRepository
    {
        private int _checks;

        public async Task<List<BudgetSummary>> GetActiveSummariesAsync(DateTime today, CancellationToken cancellationToken = default)
        {
            if (failure.On && ++_checks == 2)
            {
                throw new InvalidOperationException("Test failure after the change was saved.");
            }
            return await inner.GetActiveSummariesAsync(today, cancellationToken);
        }

        public Task<Budget?> GetWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetWithTransactionsAsync(id, cancellationToken);

        public Task<Dictionary<string, string>> GetCategoryOwnersAsync(IReadOnlyCollection<Guid> categoryIds, Guid exceptBudgetId,
            CancellationToken cancellationToken = default) => inner.GetCategoryOwnersAsync(categoryIds, exceptBudgetId, cancellationToken);

        public Task<BudgetSummary?> GetSummaryAsync(Guid id, DateTime today, CancellationToken cancellationToken = default) =>
            inner.GetSummaryAsync(id, today, cancellationToken);

        public Task<PaginatedList<BudgetSummary>> FilterAsync(BudgetFilterDto filter, DateTime today, CancellationToken cancellationToken = default) =>
            inner.FilterAsync(filter, today, cancellationToken);

        public Task<Budget> AddAsync(Budget entity, CancellationToken cancellationToken = default) => inner.AddAsync(entity, cancellationToken);

        public Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);

        public Task RemoveAsync(Budget entity, CancellationToken cancellationToken = default) => inner.RemoveAsync(entity, cancellationToken);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
    }
}

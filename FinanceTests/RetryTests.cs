using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Finance.Application.Events;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>The events on, without a broker: FinanceChanges runs each change in a transaction.</summary>
    private static FinanceApiFactory Factory(FailNextCommit failCommit) => new(services =>
    {
        services.Replace(ServiceDescriptor.Singleton(new FinanceEventsSwitch(Enabled: true)));
        services.ConfigureDbContext<FinanceDbContext>(options => options.AddInterceptors(failCommit));
    });

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

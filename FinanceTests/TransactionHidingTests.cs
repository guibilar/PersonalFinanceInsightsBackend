using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinanceTests;

/// <summary>Deleting a transaction hides it, so re-importing its statement doesn't bring it back.</summary>
public class TransactionHidingTests
{
    private const string Statement = """
        2026-01-03,INTERNET TRANSFER TO SAVINGS,500.00,
        2026-01-04,METRO,45.10,
        """;

    private static async Task<(FinanceApiFactory Factory, HttpClient Client, Guid TransferId)> ImportedAsync()
    {
        var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(Statement));
        var transferId = await factory.QueryAsync(db =>
            db.Transactions.Where(t => t.Description.StartsWith("INTERNET")).Select(t => t.Id).SingleAsync());
        return (factory, client, transferId);
    }

    private static async Task<List<string>> ListAsync(HttpClient client, bool hidden = false) =>
        (await (await client.PostAsJsonAsync("/api/Transaction/Filter", new { hidden }))
            .ReadAsync<PaginatedList<TransactionOutputDto>>()).Items.Select(t => t.Description).ToList();

    [Fact]
    public async Task Delete_HidesTheTransaction_AndAReimportDoesNotBringItBack()
    {
        var (factory, client, transferId) = await ImportedAsync();
        await using var _ = factory;

        var delete = await client.DeleteAsync($"/api/Transaction/{transferId}");
        var reimport = await (await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(Statement)))
            .ReadAsync<ImportResultDto>();

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal((2, 0, 2), (reimport.Read, reimport.Added, reimport.Skipped));
        Assert.Equal(["METRO"], await ListAsync(client));
        Assert.Equal(["INTERNET TRANSFER TO SAVINGS"], await ListAsync(client, hidden: true));
        // Still stored, once.
        Assert.Equal(2, await factory.QueryAsync(db => db.Transactions.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task HiddenTransactions_AreLeftOutOfReportsAndBudgetTotals()
    {
        await using var factory = new FinanceApiFactory();
        var budgetId = Guid.NewGuid();
        var today = DateTime.UtcNow.Date;
        Transaction Spent(string description, decimal amount, bool hidden) => new()
        {
            Id = Guid.NewGuid(), Date = DateTime.SpecifyKind(today, DateTimeKind.Utc), Description = description, Amount = amount,
            CategoryId = DefaultCategories.Dining, TransactionType = TransactionType.Debit, AccountId = Account.DefaultCreditCardId,
            Hash = Guid.NewGuid().ToString(), BudgetId = budgetId, Hidden = hidden
        };
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = budgetId, Name = "Food", Description = "d", Amount = 100 });
            db.Transactions.Add(Spent("Lunch", 20, hidden: false));
            db.Transactions.Add(Spent("Mistake", 500, hidden: true));
        });
        var client = factory.CreateClient();

        var budget = await (await client.GetAsync($"/api/Budget/{budgetId}")).ReadAsync<BudgetOutputDto>();
        var month = await (await client.GetAsync("/api/Reports/current-month-categorized-graph"))
            .ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal(20, budget.TotalSpent);
        Assert.Equal(20, Assert.Single(month.MonthlyData).Total);
    }

    [Fact]
    public async Task Restore_BringsItBack_WithItsBudgetLink()
    {
        var (factory, client, transferId) = await ImportedAsync();
        await using var _ = factory;
        var budgetId = Guid.NewGuid();
        await factory.SeedAsync(db => db.Budgets.Add(new Budget { Id = budgetId, Name = "Moving", Description = "d", Amount = 900 }));
        await client.PostAsJsonAsync("/api/Transaction/LinkTransaction", new { transactionId = transferId, budgetId });
        await client.DeleteAsync($"/api/Transaction/{transferId}");

        var restore = await client.PostAsync($"/api/Transaction/{transferId}/Restore", null);
        var restoreAgain = await client.PostAsync($"/api/Transaction/{transferId}/Restore", null);

        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, restoreAgain.StatusCode);
        Assert.Equal(["INTERNET TRANSFER TO SAVINGS", "METRO"], (await ListAsync(client)).Order());
        Assert.Empty(await ListAsync(client, hidden: true));
        var budget = await (await client.GetAsync($"/api/Budget/{budgetId}")).ReadAsync<BudgetOutputDto>();
        Assert.Equal(500, budget.TotalSpent);
    }

    [Fact]
    public async Task HideAndRestore_UnknownTransaction_Return404_AndHidingTwiceIsFine()
    {
        var (factory, client, transferId) = await ImportedAsync();
        await using var _ = factory;

        var first = await client.DeleteAsync($"/api/Transaction/{transferId}");
        var second = await client.DeleteAsync($"/api/Transaction/{transferId}");
        var unknownRestore = await client.PostAsync($"/api/Transaction/{Guid.NewGuid()}/Restore", null);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (first.StatusCode, second.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, unknownRestore.StatusCode);
    }

    [Fact]
    public async Task AddingAnIdenticalTransaction_ToAHiddenOne_SaysToRestoreIt()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var transaction = new
        {
            date = "2026-01-04", description = "METRO", amount = 45.10, categoryId = DefaultCategories.Groceries,
            transactionType = "Debit", accountId = Account.DefaultCreditCardId
        };
        var created = await (await client.PostAsJsonAsync("/api/Transaction", transaction)).ReadAsync<TransactionOutputDto>();
        await client.DeleteAsync($"/api/Transaction/{created.Id}");

        var again = await client.PostAsJsonAsync("/api/Transaction", transaction);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("restore", (await again.Content.ReadAsStringAsync()).ToLowerInvariant());
    }

    [Fact]
    public async Task DeletingABudget_AlsoUnlinksItsHiddenTransactions()
    {
        var (factory, client, transferId) = await ImportedAsync();
        await using var _ = factory;
        var budgetId = Guid.NewGuid();
        await factory.SeedAsync(db => db.Budgets.Add(new Budget { Id = budgetId, Name = "Moving", Description = "d", Amount = 900 }));
        await client.PostAsJsonAsync("/api/Transaction/LinkTransaction", new { transactionId = transferId, budgetId });
        await client.DeleteAsync($"/api/Transaction/{transferId}");

        // On PostgreSQL the foreign key rejects the delete if a hidden transaction still points at the budget.
        var delete = await client.DeleteAsync($"/api/Budget/{budgetId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Null(await factory.QueryAsync(db =>
            db.Transactions.IgnoreQueryFilters().Where(t => t.Id == transferId).Select(t => t.BudgetId).SingleAsync()));
    }
}

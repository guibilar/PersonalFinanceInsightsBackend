using System.Net;
using System.Net.Http.Json;
using FinanceDomain.Models;
using FinanceDto.Enums;
using FinanceDto.Models;
using FinanceDto.Outputs;
using Microsoft.EntityFrameworkCore;

namespace FinanceTests;

public class ApiTests : IDisposable
{
    private readonly FinanceApiFactory _factory = new();
    private readonly HttpClient _client;

    public ApiTests() => _client = _factory.CreateClient();

    public void Dispose() => _factory.Dispose();

    private static Transaction NewTransaction(string description, decimal amount, DateTime date, Guid? budgetId = null) => new()
    {
        Id = Guid.NewGuid(),
        Description = description,
        Amount = amount,
        Date = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        Category = Category.Dining,
        TransactionType = TransactionType.Debit,
        Account = AccountType.CreditCard,
        Hash = Guid.NewGuid().ToString(),
        BudgetId = budgetId
    };

    private async Task<Guid> SeedBudgetWithTransactionAsync(decimal spent = 25)
    {
        var budgetId = Guid.NewGuid();
        await _factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = budgetId, Name = "Food", Description = "Groceries", Amount = 100 });
            db.Transactions.Add(NewTransaction("Coffee", spent, DateTime.UtcNow, budgetId));
        });
        return budgetId;
    }

    // ---- Reports

    [Theory]
    [InlineData("current-month-categorized-graph")]
    [InlineData("monthly-categorized-graph")]
    public async Task Reports_WithNoTransactions_ReturnEmptyReport(string report)
    {
        var response = await _client.GetAsync($"/api/Reports/{report}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.ReadAsync<MonthlyCategorizedGraphData>();
        Assert.Empty(data.MonthlyData);
        Assert.Equal(0, data.AverageMonthlyTotal);
    }

    [Fact]
    public async Task CurrentMonthReport_IncludesTheLastDayOfTheMonth()
    {
        var now = DateTime.UtcNow;
        var lastDayEvening = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 23, 59, 59, 500);
        await _factory.SeedAsync(db => db.Transactions.Add(NewTransaction("Late dinner", 40, lastDayEvening)));

        var data = await (await _client.GetAsync("/api/Reports/current-month-categorized-graph"))
            .ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal(40, Assert.Single(data.MonthlyData).Total);
    }

    // ---- Budgets

    [Fact]
    public async Task GetBudget_IncludesLinkedTransactionsInTotals()
    {
        var budgetId = await SeedBudgetWithTransactionAsync(spent: 25);

        var budget = await (await _client.GetAsync($"/api/Budget/{budgetId}")).ReadAsync<BudgetOutputDto>();

        Assert.Equal(25, budget.TotalSpent);
        Assert.Equal(75, budget.TotalRemaining);
    }

    [Fact]
    public async Task UpdateBudget_KeepsTotalsAndLinkedTransactions()
    {
        var budgetId = await SeedBudgetWithTransactionAsync(spent: 25);

        var response = await _client.PutAsJsonAsync($"/api/Budget/{budgetId}",
            new { name = "Food 2", description = "Groceries", status = "Active", amount = 200 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var budget = await response.ReadAsync<BudgetOutputDto>();
        Assert.Equal("Food 2", budget.Name);
        Assert.Equal(25, budget.TotalSpent);
        Assert.Equal(1, await _factory.QueryAsync(db => db.Transactions.CountAsync(t => t.BudgetId == budgetId)));
    }

    [Fact]
    public async Task UpdateBudget_Unknown_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"/api/Budget/{Guid.NewGuid()}",
            new { name = "x", description = "x", status = "Active", amount = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBudget_UnlinksItsTransactions()
    {
        var budgetId = await SeedBudgetWithTransactionAsync();

        var response = await _client.DeleteAsync($"/api/Budget/{budgetId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await _factory.QueryAsync(db => db.Budgets.AnyAsync()));
        Assert.Null(await _factory.QueryAsync(db => db.Transactions.Select(t => t.BudgetId).SingleAsync()));
    }

    [Fact]
    public async Task DeleteBudget_Unknown_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/Budget/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Groceries", 100)]
    [InlineData("Food", "", 100)]
    [InlineData("Food", "Groceries", 0)]
    [InlineData("Food", "Groceries", -5)]
    public async Task CreateBudget_Invalid_Returns400(string name, string description, decimal amount)
    {
        var response = await _client.PostAsJsonAsync("/api/Budget", new { name, description, status = "Active", amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FilterBudgets_ByDescription()
    {
        await _factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = Guid.NewGuid(), Name = "A", Description = "Weekly groceries", Amount = 1 });
            db.Budgets.Add(new Budget { Id = Guid.NewGuid(), Name = "B", Description = "Travel", Amount = 1 });
        });

        var result = await (await _client.PostAsJsonAsync("/api/Budget/Filter", new { description = "GROCER" }))
            .ReadAsync<PaginatedList<BudgetOutputDto>>();

        Assert.Equal("A", Assert.Single(result.Items).Name);
    }

    [Theory]
    [InlineData("/api/Budget/Filter", 0, 10)]
    [InlineData("/api/Budget/Filter", 1, 0)]
    [InlineData("/api/Budget/Filter", 1, 1001)]
    [InlineData("/api/Transaction/Filter", 0, 10)]
    [InlineData("/api/Transaction/Filter", 1, 0)]
    public async Task Filter_InvalidPaging_Returns400(string url, int page, int pageSize)
    {
        var response = await _client.PostAsJsonAsync(url, new { page, pageSize });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Transactions

    [Fact]
    public async Task FilterTransactions_DescriptionMatchesPartiallyAndIgnoresCase()
    {
        await _factory.SeedAsync(db =>
        {
            db.Transactions.Add(NewTransaction("COFFEE SHOP", 5, DateTime.UtcNow));
            db.Transactions.Add(NewTransaction("GROCERIES", 50, DateTime.UtcNow));
        });

        var result = await (await _client.PostAsJsonAsync("/api/Transaction/Filter", new { description = "coffee" }))
            .ReadAsync<PaginatedList<TransactionOutputDto>>();

        Assert.Equal("COFFEE SHOP", Assert.Single(result.Items).Description);
    }

    [Fact]
    public async Task FilterTransactions_EndDateIncludesTheWholeDay()
    {
        await _factory.SeedAsync(db =>
        {
            db.Transactions.Add(NewTransaction("Evening", 5, new DateTime(2026, 3, 10, 18, 30, 0)));
            db.Transactions.Add(NewTransaction("Next day", 5, new DateTime(2026, 3, 11, 0, 0, 0)));
        });

        var result = await (await _client.PostAsJsonAsync("/api/Transaction/Filter",
                new { startDate = "2026-03-10", endDate = "2026-03-10" }))
            .ReadAsync<PaginatedList<TransactionOutputDto>>();

        Assert.Equal("Evening", Assert.Single(result.Items).Description);
    }

    [Fact]
    public async Task CreateTransaction_HashIsComputedByTheServer()
    {
        object Tx(string description) => new
        {
            date = "2026-09-10", description, amount = 5, category = "Dining",
            transactionType = "Debit", account = "CreditCard", hash = "client-value-is-ignored"
        };

        var first = await _client.PostAsJsonAsync("/api/Transaction", Tx("COFFEE"));
        var different = await _client.PostAsJsonAsync("/api/Transaction", Tx("GROCERIES"));
        var duplicate = await _client.PostAsJsonAsync("/api/Transaction", Tx("COFFEE"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, different.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task LinkTransaction_ReturnsNotFoundAndConflict()
    {
        var budgetId = await SeedBudgetWithTransactionAsync();
        var linkedId = await _factory.QueryAsync(db => db.Transactions.Select(t => t.Id).SingleAsync());

        var unknownTransaction = await _client.PostAsJsonAsync("/api/Transaction/LinkTransaction",
            new { transactionId = Guid.NewGuid(), budgetId });
        var unknownBudget = await _client.PostAsJsonAsync("/api/Transaction/LinkTransaction",
            new { transactionId = linkedId, budgetId = Guid.NewGuid() });
        var alreadyLinked = await _client.PostAsJsonAsync("/api/Transaction/LinkTransaction",
            new { transactionId = linkedId, budgetId });

        Assert.Equal(HttpStatusCode.NotFound, unknownTransaction.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, alreadyLinked.StatusCode);
        // Checked after the "already linked" rule, which fires first for this transaction.
        Assert.Equal(HttpStatusCode.Conflict, unknownBudget.StatusCode);
    }

    [Fact]
    public async Task LinkThenUnlink_RoundTrips()
    {
        var budgetId = Guid.NewGuid();
        var transaction = NewTransaction("Coffee", 5, DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = budgetId, Name = "Food", Description = "d", Amount = 10 });
            db.Transactions.Add(transaction);
        });

        var unknownBudget = await _client.PostAsJsonAsync("/api/Transaction/LinkTransaction",
            new { transactionId = transaction.Id, budgetId = Guid.NewGuid() });
        var link = await _client.PostAsJsonAsync("/api/Transaction/LinkTransaction",
            new { transactionId = transaction.Id, budgetId });
        var linked = await _factory.QueryAsync(db => db.Transactions.Select(t => t.BudgetId).SingleAsync());
        var unlink = await _client.PostAsync($"/api/Transaction/UnlinkTransaction/{transaction.Id}", null);
        var unlinkUnknown = await _client.PostAsync($"/api/Transaction/UnlinkTransaction/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.NotFound, unknownBudget.StatusCode);
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        Assert.Equal(budgetId, linked);
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);
        Assert.Null(await _factory.QueryAsync(db => db.Transactions.Select(t => t.BudgetId).SingleAsync()));
        Assert.Equal(HttpStatusCode.NotFound, unlinkUnknown.StatusCode);
    }

    [Fact]
    public async Task DeleteTransaction_Unknown_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/Transaction/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

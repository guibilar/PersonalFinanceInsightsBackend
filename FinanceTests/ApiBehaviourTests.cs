using System.Net;
using System.Net.Http.Json;
using Finance.Application.Abstractions;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

/// <summary>Error format, status codes and response shapes introduced with the API modernisation.</summary>
public class ApiBehaviourTests
{
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

    [Fact]
    public async Task NotFound_IsReturnedAsProblemDetails()
    {
        using var factory = new FinanceApiFactory();
        var id = Guid.NewGuid();

        var response = await factory.CreateClient().GetAsync($"/api/Budget/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Equal(404, problem.Status);
        Assert.Contains(id.ToString(), problem.Detail);
    }

    [Fact]
    public async Task UnexpectedErrors_Return500WithoutInternalDetails()
    {
        using var factory = new FinanceApiFactory(services =>
            services.Replace(ServiceDescriptor.Scoped<IStatementParser, ThrowingParser>()));
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent("x"u8.ToArray()), "file", "statement.csv" },
            { new StringContent("Chequing"), "account" }
        };

        var response = await factory.CreateClient().PostAsync("/api/Transaction/Import", form);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-path", body);
        Assert.Equal(500, (await response.ReadAsync<ProblemDetails>()).Status);
    }

    [Fact]
    public async Task CreateBudget_Returns201WithLocation()
    {
        using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Budget",
            new { name = " Food ", description = "Groceries", status = "Active", amount = 100 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<BudgetOutputDto>();
        Assert.Equal("Food", created.Name);
        var fetched = await (await client.GetAsync(response.Headers.Location)).ReadAsync<BudgetOutputDto>();
        Assert.Equal(created.Id, fetched.Id);
    }

    [Fact]
    public async Task BudgetFilter_TotalsAreSummedPerBudget()
    {
        using var factory = new FinanceApiFactory();
        var food = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = food, Name = "Food", Description = "d", Amount = 200 });
            db.Budgets.Add(new Budget { Id = Guid.NewGuid(), Name = "Travel", Description = "d", Amount = 50 });
            db.Transactions.Add(NewTransaction("A", 30, DateTime.UtcNow, food));
            db.Transactions.Add(NewTransaction("B", 20, DateTime.UtcNow, food));
            db.Transactions.Add(NewTransaction("C", 99, DateTime.UtcNow));
        });

        var page = await (await factory.CreateClient().PostAsJsonAsync("/api/Budget/Filter", new { }))
            .ReadAsync<PaginatedList<BudgetOutputDto>>();

        var foodBudget = page.Items.Single(b => b.Name == "Food");
        Assert.Equal(50, foodBudget.TotalSpent);
        Assert.Equal(25, foodBudget.PercentageUsed);
        Assert.Equal(0, page.Items.Single(b => b.Name == "Travel").TotalSpent);
    }

    [Fact]
    public async Task Transactions_ExposeLinkedBudgetAsReference_AndNullWhenUnlinked()
    {
        using var factory = new FinanceApiFactory();
        var budgetId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = budgetId, Name = "Food", Description = "d", Amount = 10 });
            db.Transactions.Add(NewTransaction("Linked", 5, DateTime.UtcNow, budgetId));
            db.Transactions.Add(NewTransaction("Unlinked", 5, DateTime.UtcNow.AddMinutes(-1)));
        });

        var page = await (await factory.CreateClient().PostAsJsonAsync("/api/Transaction/Filter", new { }))
            .ReadAsync<PaginatedList<TransactionOutputDto>>();

        var linked = page.Items.Single(t => t.Description == "Linked");
        Assert.Equal(budgetId, linked.BudgetId);
        Assert.Equal("Food", linked.Budget?.Name);
        var unlinked = page.Items.Single(t => t.Description == "Unlinked");
        Assert.Null(unlinked.BudgetId);
        Assert.Null(unlinked.Budget);
    }

    [Fact]
    public async Task Reports_UseTheInjectedClock()
    {
        var now = new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);
        using var factory = new FinanceApiFactory(services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(now))));
        await factory.SeedAsync(db =>
        {
            db.Transactions.Add(NewTransaction("Last year", 10, new DateTime(2025, 12, 31, 23, 0, 0)));
            db.Transactions.Add(NewTransaction("January", 20, new DateTime(2026, 1, 5)));
            db.Transactions.Add(NewTransaction("February", 30, new DateTime(2026, 2, 28, 23, 59, 0)));
            db.Transactions.Add(NewTransaction("March", 40, new DateTime(2026, 3, 1)));
        });
        var client = factory.CreateClient();

        var month = await (await client.GetAsync("/api/Reports/current-month-categorized-graph")).ReadAsync<MonthlyCategorizedGraphData>();
        var year = await (await client.GetAsync("/api/Reports/monthly-categorized-graph")).ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal(("Feb 2026", 30m), (Assert.Single(month.MonthlyData).Month, month.MonthlyData[0].Total));
        Assert.Equal(["Jan 2026", "Feb 2026", "Mar 2026"], year.MonthlyData.Select(m => m.Month));
        Assert.Equal(30, year.AverageMonthlyTotal);
    }

    [Fact]
    public async Task Health_ReportsHealthyWhenTheDatabaseIsReachable()
    {
        using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    private class ThrowingParser : IStatementParser
    {
        public List<Transaction> Parse(Stream statement, AccountType account) =>
            throw new InvalidDataException("Bad row in '/secret-path/cibc_credit.csv'");
    }
}

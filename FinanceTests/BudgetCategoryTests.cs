using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

/// <summary>Monthly budgets that count this month's spending in their categories by themselves.</summary>
public class BudgetCategoryTests
{
    // 02:00 UTC on April 1st is still March 31st in Toronto, so "this month" is March.
    private static FinanceApiFactory FactoryOnMarch31() => new(services =>
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-04-01T02:00:00Z")))));

    private static Transaction Tx(DateTime day, decimal amount, Guid category, Flow flow = Flow.Spending, Guid? budgetId = null,
        TransactionType type = TransactionType.Debit) => new()
    {
        Id = Guid.NewGuid(), Date = DateTime.SpecifyKind(day, DateTimeKind.Utc), Description = "SHOP", Amount = amount,
        CategoryId = category, TransactionType = type, Flow = flow, AccountId = Account.DefaultCreditCardId,
        Hash = Guid.NewGuid().ToString(), BudgetId = budgetId
    };

    private static async Task<BudgetOutputDto> CreateAsync(HttpClient client, string name, string period, params string[] categories)
    {
        var response = await client.PostAsJsonAsync("/api/Budget",
            new { name, description = "d", status = "Active", amount = 100, period,
                categoryIds = categories.Select(c => DefaultCategories.IdOf(c)!.Value) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<BudgetOutputDto>();
    }

    [Fact]
    public async Task ACategoryBudget_CountsThisMonthsSpendingInItsCategories_WithoutLinking()
    {
        await using var factory = FactoryOnMarch31();
        var client = factory.CreateClient();
        var dining = await CreateAsync(client, "Dining", "Monthly", "Dining");
        await factory.SeedAsync(db =>
        {
            db.Transactions.Add(Tx(new DateTime(2026, 3, 1), 20m, DefaultCategories.Dining));
            db.Transactions.Add(Tx(new DateTime(2026, 3, 31), 5m, DefaultCategories.Dining));
            db.Transactions.Add(Tx(new DateTime(2026, 3, 10), 8m, DefaultCategories.Dining, Flow.Refund, type: TransactionType.Credit));
            db.Transactions.Add(Tx(new DateTime(2026, 2, 27), 50m, DefaultCategories.Dining));       // last month
            db.Transactions.Add(Tx(new DateTime(2026, 3, 12), 40m, DefaultCategories.Groceries));    // another category
            db.Transactions.Add(Tx(new DateTime(2026, 3, 13), 30m, DefaultCategories.Dining, Flow.Transfer));
            db.Transactions.Add(Tx(new DateTime(2026, 3, 14), 30m, DefaultCategories.Dining, Flow.Ignored));
        });

        var budget = await (await client.GetAsync($"/api/Budget/{dining.Id}")).ReadAsync<BudgetOutputDto>();
        var page = await (await client.PostAsJsonAsync("/api/Budget/Filter", new { })).ReadAsync<PaginatedList<BudgetOutputDto>>();

        Assert.Equal((17m, 83m), (budget.TotalSpent, budget.TotalRemaining));
        Assert.Equal([("Dining", "#1F76B5")], budget.Categories.Select(c => (c.Name, c.Color)));
        Assert.Equal(17m, page.Items.Single().TotalSpent);
    }

    [Fact]
    public async Task ADeletedTransaction_NoLongerCounts_AndAnotherBudgetsLinkStaysThere()
    {
        await using var factory = FactoryOnMarch31();
        var client = factory.CreateClient();
        var dining = await CreateAsync(client, "Dining", "Monthly", "Dining");
        var trip = await CreateAsync(client, "Trip", "Total");
        await factory.SeedAsync(db =>
        {
            var hidden = Tx(new DateTime(2026, 3, 2), 20m, DefaultCategories.Dining);
            hidden.Hidden = true;
            db.Transactions.Add(hidden);
            db.Transactions.Add(Tx(new DateTime(2026, 3, 3), 30m, DefaultCategories.Dining, budgetId: trip.Id));
            db.Transactions.Add(Tx(new DateTime(2026, 3, 4), 7m, DefaultCategories.Dining));
        });

        var diningBudget = await (await client.GetAsync($"/api/Budget/{dining.Id}")).ReadAsync<BudgetOutputDto>();
        var tripBudget = await (await client.GetAsync($"/api/Budget/{trip.Id}")).ReadAsync<BudgetOutputDto>();

        Assert.Equal((7m, 30m), (diningBudget.TotalSpent, tripBudget.TotalSpent));
    }

    [Fact]
    public async Task ALinkedTransactionInTheCategory_IsCountedOnce()
    {
        await using var factory = FactoryOnMarch31();
        var client = factory.CreateClient();
        var dining = await CreateAsync(client, "Dining", "Monthly", "Dining");
        await factory.SeedAsync(db =>
        {
            db.Transactions.Add(Tx(new DateTime(2026, 3, 3), 30m, DefaultCategories.Dining, budgetId: dining.Id));
            db.Transactions.Add(Tx(new DateTime(2026, 3, 4), 7m, DefaultCategories.Dining));
        });

        var budget = await (await client.GetAsync($"/api/Budget/{dining.Id}")).ReadAsync<BudgetOutputDto>();

        Assert.Equal(37m, budget.TotalSpent);
    }

    [Fact]
    public async Task OnlyAMonthlyBudgetCanHaveCategories()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Budget",
            new { name = "Trip", description = "d", status = "Active", amount = 100, period = "Total", categoryIds = new[] { DefaultCategories.Shopping } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("monthly", await response.Content.ReadAsStringAsync());
        Assert.Empty(await factory.QueryAsync(db => db.Budgets.ToListAsync()));
    }

    [Fact]
    public async Task ACategoryBelongsToOneBudget_ButCanMoveOnceFreed()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var dining = await CreateAsync(client, "Dining", "Monthly", "Dining");

        var taken = await client.PostAsJsonAsync("/api/Budget",
            new { name = "Eating out", description = "d", status = "Active", amount = 100, period = "Monthly", categoryIds = new[] { DefaultCategories.Dining, DefaultCategories.Groceries } });
        var sameBudgetAgain = await client.PutAsJsonAsync($"/api/Budget/{dining.Id}",
            new { name = "Dining", description = "d", status = "Active", amount = 100, period = "Monthly", categoryIds = new[] { DefaultCategories.Dining, DefaultCategories.Groceries } });
        var freed = await client.PutAsJsonAsync($"/api/Budget/{dining.Id}",
            new { name = "Dining", description = "d", status = "Active", amount = 100, period = "Monthly", categoryIds = Array.Empty<Guid>() });
        var nowFree = await client.PostAsJsonAsync("/api/Budget",
            new { name = "Eating out", description = "d", status = "Active", amount = 100, period = "Monthly", categoryIds = new[] { DefaultCategories.Dining } });

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Contains("Dining is already counted by the budget", await taken.Content.ReadAsStringAsync());
        Assert.Equal([DefaultCategories.Dining, DefaultCategories.Groceries],
            (await sameBudgetAgain.ReadAsync<BudgetOutputDto>()).Categories.Select(c => c.Id));
        Assert.Empty((await freed.ReadAsync<BudgetOutputDto>()).Categories);
        Assert.Equal(HttpStatusCode.Created, nowFree.StatusCode);
    }

    [Fact]
    public async Task DeletingABudget_FreesItsCategories()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var dining = await CreateAsync(client, "Dining", "Monthly", "Dining");

        var deleted = await client.DeleteAsync($"/api/Budget/{dining.Id}");
        var again = await client.PostAsJsonAsync("/api/Budget",
            new { name = "Dining", description = "d", status = "Active", amount = 100, period = "Monthly", categoryIds = new[] { DefaultCategories.Dining } });

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task AnUnknownCategory_IsRefused()
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Budget",
            new { name = "X", description = "d", status = "Active", amount = 100, period = "Monthly", categoryIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

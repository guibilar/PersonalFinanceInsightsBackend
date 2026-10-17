using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

/// <summary>Budgets for a merchant, and for a year or a stretch of days: spending counts by itself, without linking.</summary>
public class BudgetMerchantTests
{
    // Mid-September 2026, in Toronto's time.
    private static FinanceApiFactory FactoryOnSeptember15() => new(services =>
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-09-15T16:00:00Z")))));

    private static Transaction Tx(DateTime day, string description, decimal amount, Flow flow = Flow.Spending, Guid? budgetId = null,
        bool hidden = false) => new()
    {
        Id = Guid.NewGuid(), Date = DateTime.SpecifyKind(day, DateTimeKind.Utc), Description = description, Amount = amount,
        CategoryId = DefaultCategories.Groceries, Flow = flow, Hidden = hidden, BudgetId = budgetId,
        TransactionType = flow is Flow.Refund or Flow.Income ? TransactionType.Credit : TransactionType.Debit,
        AccountId = Account.DefaultCreditCardId, Hash = Guid.NewGuid().ToString()
    };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, object budget) =>
        await client.PostAsJsonAsync("/api/Budget", budget);

    private static async Task<BudgetOutputDto> CreateAsync(HttpClient client, string name, string period, string? merchant = null,
        string? startDate = null, string? endDate = null, decimal amount = 100)
    {
        var response = await PostAsync(client, new { name, description = "d", status = "Active", amount, period, merchant, startDate, endDate });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<BudgetOutputDto>();
    }

    private static async Task<BudgetOutputDto> GetAsync(HttpClient client, Guid id) =>
        await (await client.GetAsync($"/api/Budget/{id}")).ReadAsync<BudgetOutputDto>();

    [Fact]
    public async Task AMerchantBudget_CountsThisMonthsSpendingAtEveryStoreOfIt_WithoutLinking()
    {
        await using var factory = FactoryOnSeptember15();
        var client = factory.CreateClient();
        var budget = await CreateAsync(client, "No Frills", "Monthly", merchant: " no frills ");
        var trip = await CreateAsync(client, "Trip", "Total");
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(new DateTime(2026, 9, 1), "CYNTHIA'S NO FRILLS 69 CALGARY CAN", 40),
            Tx(new DateTime(2026, 9, 12), "No Frills 12 Edmonton", 25),
            Tx(new DateTime(2026, 9, 13), "NO FRILLS 12 EDMONTON", 10, Flow.Refund),
            Tx(new DateTime(2026, 8, 31), "NO FRILLS 12 EDMONTON", 500),              // last month
            Tx(new DateTime(2026, 9, 3), "SAFEWAY #8816", 300),                        // another merchant
            Tx(new DateTime(2026, 9, 4), "NO FRILLS PAYMENT", 200, Flow.Transfer),
            Tx(new DateTime(2026, 9, 5), "NO FRILLS DELETED", 99, hidden: true),
            Tx(new DateTime(2026, 9, 6), "NO FRILLS FOR THE TRIP", 60, budgetId: trip.Id)));  // counts towards the trip

        var result = await GetAsync(client, budget.Id);
        var page = await (await client.PostAsJsonAsync("/api/Budget/Filter", new { })).ReadAsync<PaginatedList<BudgetOutputDto>>();

        Assert.Equal((55m, 45m, "no frills"), (result.TotalSpent, result.TotalRemaining, result.Merchant));
        Assert.Equal(55m, page.Items.Single(b => b.Name == "No Frills").TotalSpent);
        Assert.Equal(60m, (await GetAsync(client, trip.Id)).TotalSpent);
    }

    [Fact]
    public async Task AMerchantBudget_TakesRunsOfSpacesAsOne_AsTheRulesDo()
    {
        await using var factory = FactoryOnSeptember15();
        var client = factory.CreateClient();
        var budget = await CreateAsync(client, "Coffee", "Monthly", merchant: "tim  hortons");
        // Banks pad descriptions unevenly.
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(new DateTime(2026, 9, 2), "TIM  HORTONS #55", 5),
            Tx(new DateTime(2026, 9, 3), "TIM HORTONS     #12", 3),
            Tx(new DateTime(2026, 9, 4), "TIMHORTONS", 100)));

        Assert.Equal(8m, (await GetAsync(client, budget.Id)).TotalSpent);
    }

    [Fact]
    public async Task AYearlyBudget_CountsThisCalendarYear_AndItsLinkedTransactionsToo()
    {
        await using var factory = FactoryOnSeptember15();
        var client = factory.CreateClient();
        var gym = await CreateAsync(client, "Gym", "Yearly", merchant: "gym", amount: 1000);
        var vet = await CreateAsync(client, "Vet", "Yearly", amount: 1000);
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(new DateTime(2026, 1, 1), "GYM MEMBERSHIP", 100),
            Tx(new DateTime(2026, 12, 31), "GYM LOCKER", 20),
            Tx(new DateTime(2025, 12, 31), "GYM MEMBERSHIP", 900),                     // last year
            Tx(new DateTime(2027, 1, 1), "GYM MEMBERSHIP", 900),                       // next year
            Tx(new DateTime(2026, 2, 3), "VET C", 250, budgetId: vet.Id),
            Tx(new DateTime(2025, 6, 3), "VET C", 400, budgetId: vet.Id)));            // linked, but last year

        Assert.Equal((120m, 880m), ((await GetAsync(client, gym.Id)).TotalSpent, (await GetAsync(client, gym.Id)).TotalRemaining));
        Assert.Equal(250m, (await GetAsync(client, vet.Id)).TotalSpent);
    }

    [Fact]
    public async Task ACustomBudget_CountsFromItsFirstDayToItsLast_BothIncluded()
    {
        await using var factory = FactoryOnSeptember15();
        var client = factory.CreateClient();
        var budget = await CreateAsync(client, "Holiday food", "Custom", merchant: "restaurant",
            startDate: "2026-07-01", endDate: "2026-08-15");
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(new DateTime(2026, 6, 30), "RESTAURANT A", 500),
            Tx(new DateTime(2026, 7, 1), "RESTAURANT B", 30),
            Tx(new DateTime(2026, 8, 15), "RESTAURANT C", 20),
            Tx(new DateTime(2026, 8, 16), "RESTAURANT D", 500)));

        var result = await GetAsync(client, budget.Id);

        Assert.Equal(50m, result.TotalSpent);
        Assert.Equal((new DateTime(2026, 7, 1), new DateTime(2026, 8, 15)), (result.StartDate, result.EndDate)!);
    }

    [Fact]
    public async Task AMerchantCanHaveBudgetsForSeveralPeriods_AndACategoryBudgetCountsTheYearToo()
    {
        await using var factory = FactoryOnSeptember15();
        var client = factory.CreateClient();
        var month = await CreateAsync(client, "No Frills a month", "Monthly", merchant: "no frills");
        var year = await CreateAsync(client, "No Frills a year", "Yearly", merchant: "no frills");
        var response = await PostAsync(client, new
        {
            name = "Groceries", description = "d", status = "Active", amount = 100, period = "Yearly",
            categoryIds = new[] { DefaultCategories.Groceries }
        });
        var groceries = await response.ReadAsync<BudgetOutputDto>();
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(new DateTime(2026, 9, 2), "NO FRILLS", 10),
            Tx(new DateTime(2026, 3, 2), "NO FRILLS", 20)));

        Assert.Equal(10m, (await GetAsync(client, month.Id)).TotalSpent);
        Assert.Equal(30m, (await GetAsync(client, year.Id)).TotalSpent);
        Assert.Equal(30m, (await GetAsync(client, groceries.Id)).TotalSpent);
    }

    [Fact]
    public async Task UpdatingABudget_ChangesItsPeriodDatesAndMerchant_AndDatesOfOtherPeriodsAreDropped()
    {
        await using var factory = FactoryOnSeptember15();
        var client = factory.CreateClient();
        var budget = await CreateAsync(client, "Food", "Total");

        async Task<BudgetOutputDto> UpdateAsync(object body)
        {
            var response = await client.PutAsJsonAsync($"/api/Budget/{budget.Id}", body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadAsync<BudgetOutputDto>();
        }

        var custom = await UpdateAsync(new
        {
            name = "Food", description = "d", status = "Active", amount = 100, period = "Custom", merchant = "metro",
            startDate = "2026-07-01", endDate = "2026-07-31"
        });
        var monthly = await UpdateAsync(new
        {
            name = "Food", description = "d", status = "Active", amount = 100, period = "Monthly", merchant = "  ",
            startDate = "2026-07-01", endDate = "2026-07-31"
        });

        Assert.Equal(("Custom", "metro", new DateTime(2026, 7, 1)), (custom.Period.ToString(), custom.Merchant, custom.StartDate!.Value));
        Assert.Equal(("Monthly", null, null), (monthly.Period.ToString(), monthly.Merchant, (DateTime?)monthly.StartDate));
        Assert.Null(monthly.EndDate);
    }

    [Theory]
    [InlineData("Custom", "metro", null, null, null)]                                   // no dates
    [InlineData("Custom", "metro", "2026-07-01", null, null)]                           // no end
    [InlineData("Custom", "metro", "2026-08-01", "2026-07-01", null)]                   // ends before it starts
    [InlineData("Total", "metro", null, null, null)]                                    // no period to count
    [InlineData("Monthly", "metro", null, null, "Dining")]                              // a merchant and categories
    [InlineData("Total", null, null, null, "Dining")]                                   // categories, no period
    public async Task ABudgetThatCantCount_Returns400(string period, string? merchant, string? startDate, string? endDate, string? category)
    {
        await using var factory = FactoryOnSeptember15();

        var response = await PostAsync(factory.CreateClient(), new
        {
            name = "Bad", description = "d", status = "Active", amount = 100, period, merchant, startDate, endDate,
            categoryIds = category is null ? [] : new[] { DefaultCategories.IdOf(category)!.Value }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AMerchantLongerThanTheLimit_Returns400()
    {
        await using var factory = FactoryOnSeptember15();

        var response = await PostAsync(factory.CreateClient(), new
        {
            name = "Bad", description = "d", status = "Active", amount = 100, period = "Monthly", merchant = new string('x', 101)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

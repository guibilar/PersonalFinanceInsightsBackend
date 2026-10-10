using System.Net;
using System.Net.Http.Json;
using Finance.Application.Reports;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

/// <summary>Recurring payments, the month's forecast, what no rule sorts yet, bulk changes, and reviews.</summary>
public class InsightTests
{
    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    // Mid-October 2026, in Toronto's time.
    private static readonly DateTime Today = Day(2026, 10, 15);

    private static FinanceApiFactory FactoryOnOctober15() => new(services =>
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-10-15T16:00:00Z")))));

    private static Transaction Tx(DateTime date, string description, decimal amount, Guid? categoryId = null, Flow flow = Flow.Spending,
        Guid? accountId = null, bool categoryLocked = false) => new()
    {
        Id = Guid.NewGuid(), Date = date, Description = description, Amount = amount, Flow = flow, CategoryLocked = categoryLocked,
        TransactionType = flow is Flow.Income or Flow.Refund ? TransactionType.Credit : TransactionType.Debit,
        CategoryId = categoryId ?? DefaultCategories.Groceries, AccountId = accountId ?? Account.DefaultCreditCardId,
        Hash = Guid.NewGuid().ToString()
    };

    private static Charge C(DateTime date, string merchant, decimal amount) => new(Guid.NewGuid(), date, amount, merchant, null);

    // ---- Finding recurring payments

    [Fact]
    public void AMonthlyPrice_ThatWentUp_IsActive_WithItsChange_AndNextDate()
    {
        var charges = new[] { 6, 7, 8, 9 }.Select(m => C(Day(2026, m, 5), "NETFLIX.COM", 16.49m))
            .Append(C(Day(2026, 10, 5), "NETFLIX.COM", 18.99m));

        var payment = Assert.Single(RecurringDetector.Detect(charges, Today)).Payment;

        Assert.Equal((RecurringCadence.Monthly, RecurringStatus.Active, 18.99m, false), (payment.Cadence, payment.Status, payment.Amount, payment.VariableAmount));
        Assert.Equal((16.49m, 18.99m, Day(2026, 10, 5)), (payment.PriceChange!.From, payment.PriceChange.To, payment.PriceChange.Since));
        Assert.Equal((Day(2026, 11, 5), 18.99m, 227.88m, 5), (payment.NextDate, payment.MonthlyCost, payment.YearlyCost, payment.Count));
    }

    [Fact]
    public void WeeklyGroceries_ForDifferentAmounts_AreNotASubscription()
    {
        var amounts = new[] { 85m, 120m, 60m, 140m, 95m, 70m };
        var charges = amounts.Select((a, i) => C(Day(2026, 9, 1).AddDays(7 * i), "NO FRILLS", a));

        Assert.Empty(RecurringDetector.Detect(charges, Today));
    }

    [Fact]
    public void TwoSubscriptionsFromOneMerchant_AreFoundPriceByPrice()
    {
        var charges = Enumerable.Range(6, 4).SelectMany(m => new[]
        {
            C(Day(2026, m, 1), "APPLE.COM/BILL", 2.99m),
            C(Day(2026, m, 10), "APPLE.COM/BILL", 12.99m),
        });

        var found = RecurringDetector.Detect(charges, Day(2026, 9, 20)).Select(d => d.Payment).OrderBy(p => p.Amount).ToList();

        Assert.Equal([(2.99m, RecurringCadence.Monthly), (12.99m, RecurringCadence.Monthly)], found.Select(p => (p.Amount, p.Cadence)));
    }

    [Fact]
    public void AYearlyRenewal_NeedsTwoCharges()
    {
        var payment = Assert.Single(RecurringDetector.Detect([C(Day(2025, 3, 1), "DOMAIN", 20), C(Day(2026, 3, 1), "DOMAIN", 20)], Today)).Payment;

        Assert.Equal((RecurringCadence.Yearly, Day(2027, 3, 1), 1.67m), (payment.Cadence, payment.NextDate, payment.MonthlyCost));
    }

    [Fact]
    public void OneDueAWhileAgo_IsLate_AndOneDueLongAgo_HasStopped()
    {
        var late = Enumerable.Range(6, 4).Select(m => C(Day(2026, m, 1), "PHONE", 10)); // next was Oct 1
        var stopped = Enumerable.Range(1, 5).Select(m => C(Day(2026, m, 3), "GYM", 50)); // next was Jun 3

        var found = RecurringDetector.Detect(late.Concat(stopped), Today).Select(d => d.Payment).ToDictionary(p => p.Name, p => p.Status);

        Assert.Equal(RecurringStatus.Late, found["PHONE"]);
        Assert.Equal(RecurringStatus.Stopped, found["GYM"]);
    }

    [Fact]
    public void ABillThatVaries_IsRecurring_AtTheAverageOfItsLastThree()
    {
        var charges = new[] { 80m, 95m, 70m, 88m }.Select((a, i) => C(Day(2026, 6 + i, 12), "HYDRO", a));

        var payment = Assert.Single(RecurringDetector.Detect(charges, Today)).Payment;

        Assert.Equal((true, 84.33m, null), (payment.VariableAmount, payment.Amount, payment.PriceChange));
    }

    [Theory]
    [InlineData("SQ *CORNER STORE #123 TORONTO ON", "CORNER STORE")]
    [InlineData("NETFLIX.COM 866-579-7172 CA", "NETFLIX.COM")]
    [InlineData("7-ELEVEN 1234", "7-ELEVEN 1234")]
    public void AMerchant_IsTheStartOfItsDescription(string description, string merchant) =>
        Assert.Equal(merchant, MerchantNames.Normalize(description));

    // ---- The reports

    /// <summary>
    /// July to mid-October (the seeded rules name Netflix "NETFLIX"): groceries twice a month for amounts that vary as shopping does ($310, $310, $300: $10 a
    /// day), Netflix on the 5th and rent on the 20th; October so far has Netflix and $40 of groceries, and the rent is
    /// still to come.
    /// </summary>
    private static void SeedHistory(Finance.Infrastructure.Persistence.FinanceDbContext db)
    {
        var groceries = new Dictionary<int, (decimal NoFrills, decimal Safeway)> { [7] = (50, 260), [8] = (250, 60), [9] = (100, 200) };
        foreach (var (month, (noFrills, safeway)) in groceries)
        {
            db.Transactions.AddRange(
                Tx(Day(2026, month, 1), "NO FRILLS", noFrills),
                Tx(Day(2026, month, 11), "SAFEWAY", safeway),
                Tx(Day(2026, month, 5), "NETFLIX.COM", 15, DefaultCategories.Subscriptions),
                Tx(Day(2026, month, 20), "LANDLORD", 1000, DefaultCategories.Rent));
        }
        db.Transactions.AddRange(
            Tx(Day(2026, 10, 3), "NO FRILLS", 40),
            Tx(Day(2026, 10, 5), "NETFLIX.COM", 15, DefaultCategories.Subscriptions),
            Tx(Day(2026, 9, 25), "PAYROLL", 3000, DefaultCategories.Other, Flow.Income, Account.DefaultChequingId));
    }

    [Fact]
    public async Task Recurring_ListsWhatComesEveryMonth_WithItsTotals()
    {
        await using var factory = FactoryOnOctober15();
        await factory.SeedAsync(SeedHistory);

        var report = await (await factory.CreateClient().GetAsync("/api/Reports/recurring")).ReadAsync<RecurringReport>();

        Assert.Equal(
            [("LANDLORD", 1000m, Day(2026, 10, 20), "Rent"), ("NETFLIX", 15m, Day(2026, 11, 5), "Subscriptions")],
            report.Payments.Select(p => (p.Name, p.Amount, p.NextDate, p.Category!.Name)));
        Assert.Equal((1015m, 12180m), (report.MonthlyTotal, report.YearlyTotal));
    }

    [Fact]
    public async Task Forecast_IsSpentSoFar_PlusWhatIsDue_PlusTheUsualDayToDay()
    {
        await using var factory = FactoryOnOctober15();
        await factory.SeedAsync(SeedHistory);
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/Budget", new
        {
            name = "Food", description = "d", status = "Active", amount = 400, period = "Monthly", categoryIds = new[] { DefaultCategories.Groceries }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var forecast = await (await client.GetAsync("/api/Reports/forecast")).ReadAsync<SpendingForecast>();

        Assert.Equal(("October 2026", 31, 16), (forecast.Month, forecast.DaysInMonth, forecast.DaysLeft));
        // $55 so far, the $1,000 rent on the 20th, and 16 days at $10.
        Assert.Equal((55m, 10m, 1215m), (forecast.Spent, forecast.DailyRate, forecast.Projected));
        Assert.Equal(("LANDLORD", 1000m, Day(2026, 10, 20)), Assert.Single(forecast.Upcoming) is var c ? (c.Name, c.Amount, c.Date) : default);
        Assert.Equal((1321.67m, 3), (forecast.Usual, forecast.UsualMonths));
        var food = Assert.Single(forecast.Budgets);
        Assert.Equal(("Food", 40m, 200m, 50m), (food.Name, food.Spent, food.Projected, food.ProjectedPercentage));
    }

    [Fact]
    public async Task Forecast_WithoutACompleteMonth_ProjectsAYearlyBudget_AtItsPaceSinceTheDataStarts()
    {
        await using var factory = FactoryOnOctober15();
        // The data starts on September 20th: no month is complete yet, so the budget's own pace is all there is.
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(Day(2026, 9, 20), "NO FRILLS", 130),
            Tx(Day(2026, 10, 5), "SAFEWAY", 130)));
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/Budget", new
        {
            name = "Food", description = "d", status = "Active", amount = 3000, period = "Yearly", categoryIds = new[] { DefaultCategories.Groceries }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var forecast = await (await client.GetAsync("/api/Reports/forecast")).ReadAsync<SpendingForecast>();

        // $260 over the 26 days from September 20th is $10 a day, and 77 days are left in the year.
        var food = Assert.Single(forecast.Budgets);
        Assert.Equal((260m, 1030m), (food.Spent, food.Projected));
    }

    [Fact]
    public async Task Uncategorised_GroupsWhatNoRuleSorts_ByMerchant()
    {
        await using var factory = FactoryOnOctober15();
        var other = DefaultCategories.Other;
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(Day(2026, 10, 1), "SQ *CORNER STORE #12 TORONTO", 5, other),
            Tx(Day(2026, 10, 8), "SQ *CORNER STORE #12 TORONTO", 7, other),
            Tx(Day(2026, 10, 9), "CORNER STORE 99", 3, other),
            Tx(Day(2026, 10, 2), "MYSTERY SHOP", 20, other),
            // Picked by hand, savings, income, and one a rule sends to Other on purpose: none is missing a rule.
            Tx(Day(2026, 10, 3), "HANDPICKED", 9, other, categoryLocked: true),
            Tx(Day(2026, 10, 4), "TO SAVINGS", 100, other, Flow.Transfer, Account.DefaultSavingsId),
            Tx(Day(2026, 10, 5), "PAYROLL", 3000, other, Flow.Income, Account.DefaultChequingId),
            Tx(Day(2026, 10, 6), "KEEP ME HERE", 4, other)));
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/CategoryRules", new { keyword = "KEEP ME", categoryId = other, position = 0 })).StatusCode);

        var report = await (await client.GetAsync("/api/Reports/uncategorised")).ReadAsync<UncategorisedReport>();

        Assert.Equal(4, report.TransactionCount);
        Assert.Equal([("CORNER STORE", 3, 15m), ("MYSTERY SHOP", 1, 20m)], report.Merchants.Select(m => (m.Keyword, m.Count, m.Spending)));
        Assert.Equal(["CORNER STORE 99", "SQ *CORNER STORE #12 TORONTO"], report.Merchants[0].Examples);
    }

    [Fact]
    public async Task AMonthsReview_ComparesItWithTheMonthBefore()
    {
        await using var factory = FactoryOnOctober15();
        await factory.SeedAsync(SeedHistory);

        // Without a month: the last one that is over.
        var review = await (await factory.CreateClient().GetAsync("/api/Reports/review")).ReadAsync<ReviewReport>();

        Assert.Equal(("September 2026", "August 2026", true), (review.Label, review.PreviousLabel, review.Complete));
        Assert.Equal((1315m, 1325m, 3000m, 0m), (review.Spending, review.PreviousSpending, review.Income, review.PreviousIncome));
        // Four purchases on four days of thirty.
        Assert.Equal((4, 26), (review.Purchases, review.NoSpendDays));
        Assert.Equal(("LANDLORD", 1000m), (review.BiggestPurchase!.Description, review.BiggestPurchase.Amount));
        Assert.Equal(
            [("Rent", 1000m, 1000m), ("Groceries", 300m, 310m), ("Subscriptions", 15m, 15m)],
            review.Categories.Select(c => (c.Category.Name, c.Spending, c.PreviousSpending)));
        Assert.Equal(["LANDLORD", "SAFEWAY", "NO FRILLS", "NETFLIX"], review.TopMerchants.Select(m => m.Name));
        Assert.Equal((2, 1015m, "2026-07"), (review.RecurringCount, review.RecurringMonthlyTotal, review.Earliest));
        Assert.Empty(review.Months);
    }

    [Fact]
    public async Task AYearsReview_HasEachMonthSoFar()
    {
        await using var factory = FactoryOnOctober15();
        await factory.SeedAsync(SeedHistory);

        var review = await (await factory.CreateClient().GetAsync("/api/Reports/review?year=2026")).ReadAsync<ReviewReport>();

        Assert.Equal(("2026", "2025", false, Day(2026, 12, 31)), (review.Label, review.PreviousLabel, review.Complete, review.End));
        Assert.Equal(10, review.Months.Count);
        Assert.Equal([("Jul", 1325m), ("Aug", 1325m), ("Sep", 1315m), ("Oct", 55m)], review.Months.Skip(6).Select(m => (m.Label, m.Spending)));
    }

    [Theory]
    [InlineData("?year=2026&month=11")]
    [InlineData("?year=2026&month=13")]
    [InlineData("?year=1999")]
    [InlineData("?month=3")]
    public async Task AReviewOfAPeriodThatIsntThere_Returns400(string query)
    {
        await using var factory = FactoryOnOctober15();

        Assert.Equal(HttpStatusCode.BadRequest, (await factory.CreateClient().GetAsync($"/api/Reports/review{query}")).StatusCode);
    }

    // ---- Bulk changes

    [Fact]
    public async Task ABulkChange_SetsTheCategoryAndFlowWhereTheyFit_AndSaysWhatItSkipped()
    {
        await using var factory = FactoryOnOctober15();
        var coffee = Tx(Day(2026, 10, 1), "COFFEE", 4, DefaultCategories.Other);
        var lunch = Tx(Day(2026, 10, 2), "LUNCH", 12, DefaultCategories.Other);
        var refund = Tx(Day(2026, 10, 3), "LUNCH REFUND", 12, DefaultCategories.Other, Flow.Refund);
        await factory.SeedAsync(db => db.Transactions.AddRange(coffee, lunch, refund));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Transaction/Bulk", new
        {
            transactionIds = new[] { coffee.Id, lunch.Id, refund.Id, Guid.NewGuid() }, categoryId = DefaultCategories.Dining, flow = "Spending"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.ReadAsync<BulkUpdateResultDto>();
        Assert.Equal((3, 2), (result.Updated, result.Skipped));
        Assert.Equal(2, result.Notes.Count);
        var stored = await factory.QueryAsync(db => Task.FromResult(db.Transactions.ToDictionary(t => t.Id)));
        Assert.All(new[] { coffee, lunch, refund }, t => Assert.Equal((DefaultCategories.Dining, true), (stored[t.Id].CategoryId, stored[t.Id].CategoryLocked)));
        // Money in can't be spending: the refund kept its flow, and it isn't locked.
        Assert.Equal((Flow.Refund, false), (stored[refund.Id].Flow, stored[refund.Id].FlowLocked));
    }

    [Fact]
    public async Task ABulkChange_LinksToAnActiveBudget_AndUnlinks()
    {
        await using var factory = FactoryOnOctober15();
        var coffee = Tx(Day(2026, 10, 1), "COFFEE", 4);
        var lunch = Tx(Day(2026, 10, 2), "LUNCH", 12);
        await factory.SeedAsync(db => db.Transactions.AddRange(coffee, lunch));
        var client = factory.CreateClient();
        var trip = await (await client.PostAsJsonAsync("/api/Budget", new { name = "Trip", description = "d", status = "Active", amount = 500, period = "Total" }))
            .ReadAsync<BudgetOutputDto>();
        var closed = await (await client.PostAsJsonAsync("/api/Budget", new { name = "Closed", description = "d", status = "Inactive", amount = 500, period = "Total" }))
            .ReadAsync<BudgetOutputDto>();
        var ids = new[] { coffee.Id, lunch.Id };

        Assert.Equal(2, (await (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = ids, budgetId = trip.Id })).ReadAsync<BulkUpdateResultDto>()).Updated);
        Assert.Equal(16m, (await (await client.GetAsync($"/api/Budget/{trip.Id}")).ReadAsync<BudgetOutputDto>()).TotalSpent);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = ids, budgetId = closed.Id })).StatusCode);

        Assert.Equal(2, (await (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = ids, unlinkBudget = true })).ReadAsync<BulkUpdateResultDto>()).Updated);
        Assert.Equal(0m, (await (await client.GetAsync($"/api/Budget/{trip.Id}")).ReadAsync<BudgetOutputDto>()).TotalSpent);
    }

    [Fact]
    public async Task ABulkChange_ThatChangesNothing_OrTooMuch_Returns400()
    {
        await using var factory = FactoryOnOctober15();
        var client = factory.CreateClient();
        var one = new[] { Guid.NewGuid() };

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = one })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = Array.Empty<Guid>(), unlinkBudget = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = one, budgetId = Guid.NewGuid(), unlinkBudget = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()), unlinkBudget = true })).StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using Finance.Application.Reports;
using Finance.Contracts.Reports;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

/// <summary>Totals for what the transaction list's criteria pick, spread over weeks, months, quarters or years.</summary>
public class BreakdownTests
{
    private const string Breakdown = "/api/Reports/breakdown";

    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    // Mid-September 2026: July and August are over, September is running.
    private static FinanceApiFactory FactoryOnSeptember15() => new(services =>
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-09-15T16:00:00Z")))));

    private static Transaction Tx(DateTime date, string description, decimal amount, Flow flow = Flow.Spending,
        Guid? categoryId = null, bool hidden = false) => new()
    {
        Id = Guid.NewGuid(), Date = date, Description = description, Amount = amount, Flow = flow, Hidden = hidden,
        TransactionType = flow is Flow.Income or Flow.Refund ? TransactionType.Credit : TransactionType.Debit,
        CategoryId = categoryId ?? DefaultCategories.Groceries, AccountId = Account.DefaultCreditCardId, Hash = Guid.NewGuid().ToString()
    };

    private static async Task<TransactionBreakdown> PostAsync(HttpClient client, object input)
    {
        var response = await client.PostAsJsonAsync(Breakdown, input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<TransactionBreakdown>();
    }

    // ---- Periods

    [Theory]
    [InlineData("2026-09-14", BreakdownInterval.Week, "2026-09-14", "Sep 14")]   // a Monday
    [InlineData("2026-09-20", BreakdownInterval.Week, "2026-09-14", "Sep 14")]   // the Sunday after it
    [InlineData("2026-01-01", BreakdownInterval.Week, "2025-12-29", "Dec 29")]   // weeks cross years
    [InlineData("2026-09-30", BreakdownInterval.Month, "2026-09-01", "Sep 2026")]
    [InlineData("2026-08-31", BreakdownInterval.Quarter, "2026-07-01", "Q3 2026")]
    [InlineData("2026-12-31", BreakdownInterval.Year, "2026-01-01", "2026")]
    public void APeriod_StartsOnItsFirstDay_AndIsLabelledByIt(string date, BreakdownInterval interval, string start, string label)
    {
        var periodStart = BreakdownPeriods.StartOf(DateTime.Parse(date), interval);

        Assert.Equal((DateTime.Parse(start), label), (periodStart, BreakdownPeriods.Label(periodStart, interval)));
    }

    [Fact]
    public void Periods_AreCountedFromTheOneWithTheFirstDayToTheOneWithTheLast()
    {
        Assert.Equal(3, BreakdownPeriods.Count(Day(2026, 7, 31), Day(2026, 9, 1), BreakdownInterval.Month));
        Assert.Equal(2, BreakdownPeriods.Count(Day(2026, 9, 13), Day(2026, 9, 14), BreakdownInterval.Week));
        Assert.Equal(1, BreakdownPeriods.Count(Day(2026, 1, 1), Day(2026, 12, 31), BreakdownInterval.Year));
    }

    // ---- The report

    [Fact]
    public async Task AMerchant_ByMonth_HasEveryMonth_RunningTotals_AndAnAverageOverTheFinishedOnes()
    {
        await using var factory = FactoryOnSeptember15();
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(Day(2026, 7, 3), "CYNTHIA'S NO FRILLS 69 CALGARY CAN", 40),
            Tx(Day(2026, 7, 20), "NO FRILLS 12 EDMONTON", 60),
            Tx(Day(2026, 7, 21), "NO FRILLS 12 EDMONTON", 10, Flow.Refund),
            // nothing in August
            Tx(Day(2026, 9, 2), "CYNTHIA'S NO FRILLS 69 CALGARY CAN", 25),
            Tx(Day(2026, 9, 3), "NO FRILLS DELETED", 99, hidden: true),
            Tx(Day(2026, 9, 4), "SAFEWAY #8816", 30)));

        var result = await PostAsync(factory.CreateClient(), new { description = "no frills", interval = "Month" });

        Assert.Equal((4, 115m, 10m, 0m), (result.Count, result.Spending, result.Refunds, result.Income));
        Assert.Equal(
            [("Jul 2026", 3, 90m, 90m), ("Aug 2026", 0, 0m, 90m), ("Sep 2026", 1, 25m, 115m)],
            result.Periods.Select(p => (p.Label, p.Count, p.Spending, p.CumulativeSpending)));
        Assert.Equal((Day(2026, 7, 1), Day(2026, 7, 31)), (result.Periods[0].Start, result.Periods[0].End));
        // July and August; September isn't over.
        Assert.Equal((2, 45m), (result.AveragedPeriods, result.AverageSpending));
    }

    [Fact]
    public async Task ACategory_BetweenTwoDates_CountsBothEndDays_AndKeepsIncomeApart()
    {
        await using var factory = FactoryOnSeptember15();
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(Day(2026, 8, 31), "BEFORE", 500, categoryId: DefaultCategories.Dining),
            Tx(Day(2026, 9, 1), "FIRST DAY", 20, categoryId: DefaultCategories.Dining),
            Tx(Day(2026, 9, 7), "LAST DAY", 30, categoryId: DefaultCategories.Dining),
            Tx(Day(2026, 9, 7), "LUNCH PAID BACK", 12, Flow.Income, DefaultCategories.Dining),
            Tx(Day(2026, 9, 7), "CARD PAYMENT", 400, Flow.Transfer, DefaultCategories.Dining),
            Tx(Day(2026, 9, 8), "AFTER", 700, categoryId: DefaultCategories.Dining),
            Tx(Day(2026, 9, 3), "ANOTHER CATEGORY", 900)));

        var result = await PostAsync(factory.CreateClient(), new
        {
            categoryId = DefaultCategories.Dining, startDate = "2026-09-01", endDate = "2026-09-07", interval = "Week"
        });

        // Transfers are neither spending nor income, but they are among the matching transactions.
        Assert.Equal((4, 50m, 12m), (result.Count, result.Spending, result.Income));
        Assert.Equal(
            [("Aug 31", 20m, 0m), ("Sep 7", 30m, 12m)],
            result.Periods.Select(p => (p.Label, p.Spending, p.Income)));
        // The weeks run Monday to Sunday, but their days stay within those asked for: listing the first week (or where
        // its money went) mustn't take in August 31, which its bar doesn't count.
        Assert.Equal(
            [(Day(2026, 9, 1), Day(2026, 9, 6)), (Day(2026, 9, 7), Day(2026, 9, 7))],
            result.Periods.Select(p => (p.Start, p.End)));
    }

    [Fact]
    public async Task TopMerchants_GroupTheStoresOfAChainByTheirRule_AndTheRestByTheStartOfTheDescription()
    {
        await using var factory = FactoryOnSeptember15();
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(Day(2026, 9, 1), "CYNTHIA'S NO FRILLS 69 CALGARY CAN", 40),
            Tx(Day(2026, 9, 2), "NO FRILLS 12   EDMONTON", 60),
            Tx(Day(2026, 9, 3), "SAFEWAY #8816", 30),
            Tx(Day(2026, 9, 4), "CORNER   STORE", 5),
            Tx(Day(2026, 9, 5), "CORNER STORE", 5, Flow.Refund),
            Tx(Day(2026, 9, 6), "FARMERS MARKET", 12),
            // Without a rule, as the review groups them: the processor and the store number dropped.
            Tx(Day(2026, 9, 7), "SQ *BULK BARN #12", 9),
            Tx(Day(2026, 9, 8), "bulk barn 4401 calgary", 6)));

        var result = await PostAsync(factory.CreateClient(), new { categoryId = DefaultCategories.Groceries });

        // The corner store's refund cancels its purchase, so it spent nothing there.
        Assert.Equal(
            [("NO FRILLS", 2, 100m), ("SAFEWAY", 1, 30m), ("BULK BARN", 2, 15m), ("FARMERS MARKET", 1, 12m)],
            result.TopMerchants.Select(m => (m.Name, m.Count, m.Spending)));
        Assert.Equal(4, result.MerchantCount);
    }

    [Fact]
    public async Task TheBreakdownShowsTheTopEight_AndTheMerchantsReportPagesThroughThemAll()
    {
        await using var factory = FactoryOnSeptember15();
        // Twelve stores, the first the largest.
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Enumerable.Range(1, 12).Select(i => Tx(Day(2026, 9, 1), $"STORE {(char)('A' + i - 1)}", 130 - i * 10))));
        var client = factory.CreateClient();

        var breakdown = await PostAsync(client, new { description = "store" });
        var second = await client.PostAsJsonAsync("/api/Reports/merchants", new { description = "store", page = 2, pageSize = 8 });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var page = await second.ReadAsync<PaginatedList<MerchantTotal>>();

        Assert.Equal((8, 12), (breakdown.TopMerchants.Count, breakdown.MerchantCount));
        Assert.Equal(["STORE I", "STORE J", "STORE K", "STORE L"], page.Items.Select(m => m.Name));
        Assert.Equal((12, false), (page.TotalCount, page.HasNextPage));
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(1, 0)]
    public async Task AMerchantsPageOutOfRange_Returns400(int page, int pageSize)
    {
        await using var factory = FactoryOnSeptember15();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Reports/merchants", new { page, pageSize });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WithoutAnEndDate_ThePeriodsRunToToday()
    {
        await using var factory = FactoryOnSeptember15();
        await factory.SeedAsync(db => db.Transactions.Add(Tx(Day(2026, 6, 10), "GYM", 50)));

        var result = await PostAsync(factory.CreateClient(), new { description = "gym", interval = "Quarter" });

        Assert.Equal(["Q2 2026", "Q3 2026"], result.Periods.Select(p => p.Label));
        Assert.Equal((1, 50m), (result.AveragedPeriods, result.AverageSpending));
    }

    [Fact]
    public async Task NothingMatching_IsAnEmptyReport()
    {
        await using var factory = FactoryOnSeptember15();

        var result = await PostAsync(factory.CreateClient(), new { description = "nowhere" });

        Assert.Equal((0, 0m, 0), (result.Count, result.Spending, result.Periods.Count));
        Assert.Empty(result.TopMerchants);
    }

    [Fact]
    public async Task YearsOfWeeks_AreShownByTheNextLongerIntervalThatFits_TotalsIncluded()
    {
        await using var factory = FactoryOnSeptember15();
        await factory.SeedAsync(db => db.Transactions.Add(Tx(Day(2026, 9, 1), "GYM", 50)));
        var client = factory.CreateClient();

        // 2010 to 2026: over 800 weeks, but 200 months.
        var months = await PostAsync(client, new { startDate = "2010-01-01", endDate = "2026-09-01", interval = "Week" });
        // A typo'd year: 2,000 years of months and quarters still don't fit, so years... and those don't either.
        var tooLong = await client.PostAsJsonAsync(Breakdown, new { startDate = "0026-09-01", endDate = "2026-09-01", interval = "Week" });

        Assert.Equal((BreakdownInterval.Month, 201, 50m), (months.Interval, months.Periods.Count, months.Spending));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Theory]
    [InlineData("2026-09-10", "2026-09-01", "Month")]  // ends before it starts
    [InlineData("2026-09-01", "2026-09-10", "Fortnight")]
    // The calendar's last year: its periods would run past the end of it.
    [InlineData("9999-01-01", "9999-12-31", "Year")]
    [InlineData("9999-12-01", "9999-12-30", "Month")]
    [InlineData("2026-09-01", "9999-12-30", "Year")]
    public async Task UnusableCriteria_Return400(string startDate, string endDate, string interval)
    {
        await using var factory = FactoryOnSeptember15();

        var response = await factory.CreateClient().PostAsJsonAsync(Breakdown, new { startDate, endDate, interval });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

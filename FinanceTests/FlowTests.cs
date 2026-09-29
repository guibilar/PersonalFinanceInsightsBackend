using System.Net;
using System.Net.Http.Json;
using Finance.Application.Import;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceTests;

/// <summary>Flows (spending, refund, income, transfer, ignored): the rules, editing by hand, and the reports built on them.</summary>
public class FlowTests
{
    private const string Rules = "/api/FlowRules";

    // The initial rules, as shipped in import-rules.json: the base set, then each rule pack's.
    private static readonly List<FlowRule> SeedRules = ShippedRules();

    private static List<FlowRule> ShippedRules()
    {
        var options = new ConfigurationBuilder().AddJsonFile("import-rules.json").Build()
            .GetSection(ImportOptions.SectionName).Get<ImportOptions>()!;
        var rules = options.ToFlowRules();
        foreach (var pack in options.RulePacks)
        {
            rules.AddRange(pack.ToFlowRules(rules.Select(r => r.Keyword).ToHashSet()));
        }
        return rules;
    }

    private static Transaction Stored(string description, decimal amount, TransactionType direction, AccountType account,
        DateTime? date = null, Flow? flow = null, bool flowLocked = false) => new()
    {
        Id = Guid.NewGuid(), Date = date ?? new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), Description = description,
        Amount = amount, CategoryId = DefaultCategories.Other, TransactionType = direction, AccountId = TestAccounts.Id(account),
        Flow = flow ?? TestAccounts.DefaultFlow(account, direction), FlowLocked = flowLocked, Hash = Guid.NewGuid().ToString()
    };

    private static async Task<List<FlowRuleOutputDto>> GetRulesAsync(HttpClient client) =>
        await (await client.GetAsync(Rules)).ReadAsync<List<FlowRuleOutputDto>>();

    // ---- Rules

    [Fact]
    public async Task Startup_StoresTheRulesFromImportRulesJson_Once()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var rules = await GetRulesAsync(client);
        foreach (var rule in rules) await client.DeleteAsync($"{Rules}/{rule.Id}");
        await factory.Services.SeedRulesAsync(NullLogger.Instance);

        // Compared without regard to order: the database sorts by its own collation.
        Assert.Equal(SeedRules.OrderBy(r => r.Keyword, StringComparer.Ordinal).Select(r => (r.Keyword, r.Flow)),
            rules.OrderBy(r => r.Keyword, StringComparer.Ordinal).Select(r => (r.Keyword, r.Flow)));
        Assert.Contains(("PAYMENT THANK YOU", Flow.Transfer), rules.Select(r => (r.Keyword, r.Flow)));
        Assert.Contains(("DEEL CANADA", Flow.Income), rules.Select(r => (r.Keyword, r.Flow)));
        Assert.Empty(await GetRulesAsync(client));
    }

    [Fact]
    public async Task AddUpdateDelete_NormalisesTheKeyword_AndRefusesDuplicates()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var added = await client.PostAsJsonAsync(Rules, new { keyword = "  interest paid ", flow = "Income" });
        var duplicate = await client.PostAsJsonAsync(Rules, new { keyword = "INTEREST PAID", flow = "Transfer" });
        var rule = await added.ReadAsync<FlowRuleOutputDto>();
        var updated = await (await client.PutAsJsonAsync($"{Rules}/{rule.Id}", new { keyword = "interest", flow = "Ignored" }))
            .ReadAsync<FlowRuleOutputDto>();
        var deleted = await client.DeleteAsync($"{Rules}/{rule.Id}");

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(("INTEREST PAID", Flow.Income), (rule.Keyword, rule.Flow));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(("INTEREST", Flow.Ignored), (updated.Keyword, updated.Flow));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{Rules}/{rule.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("", "Income")]
    [InlineData("SALARY", null)]
    [InlineData("SALARY", "Spending")]
    public async Task Add_InvalidRule_Returns400(string keyword, string? flow)
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(Rules, new { keyword, flow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (flow == "Spending")
        {
            Assert.Contains("flow", (await response.ReadAsync<HttpValidationProblemDetails>()).Errors.Keys);
        }
    }

    [Fact]
    public async Task Import_UsesARuleAddedInTheApp()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await client.PostAsJsonAsync(Rules, new { keyword = "E-TRANSFER FROM MOM", flow = "Transfer" });

        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("""
            2026-01-03,E-TRANSFER FROM MOM,,200.00
            2026-01-04,E-TRANSFER FROM A FRIEND,,40.00
            """, "Chequing"));

        var flows = await factory.QueryAsync(db => db.Transactions.OrderBy(t => t.Date).Select(t => t.Flow).ToListAsync());
        Assert.Equal([Flow.Transfer, Flow.Income], flows);
    }

    [Fact]
    public async Task ApplyRules_UpdatesFlows_ButNotHandPickedOnes()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var interest = Stored("INTEREST", 3, TransactionType.Credit, AccountType.Savings);
        var handPicked = Stored("INTEREST BONUS", 5, TransactionType.Credit, AccountType.Savings, flow: Flow.Ignored, flowLocked: true);
        var cashBack = Stored("CASH BACK", 10, TransactionType.Credit, AccountType.Chequing);
        await factory.SeedAsync(db => db.Transactions.AddRange(interest, handPicked, cashBack));
        await client.PostAsJsonAsync(Rules, new { keyword = "INTEREST", flow = "Income" });
        await client.PostAsJsonAsync(Rules, new { keyword = "CASH BACK", flow = "Refund" });

        var result = await (await client.PostAsync("/api/Transaction/ApplyRules", null)).ReadAsync<RulesResultDto>();

        Assert.Equal(2, result.Updated);
        var flows = await factory.QueryAsync(db => db.Transactions.ToDictionaryAsync(t => t.Id, t => t.Flow));
        Assert.Equal(Flow.Income, flows[interest.Id]);
        Assert.Equal(Flow.Ignored, flows[handPicked.Id]);
        Assert.Equal(Flow.Refund, flows[cashBack.Id]);
    }

    // ---- Editing

    [Fact]
    public async Task Update_PickingAFlow_LocksIt_AndLeavingItOutKeepsIt()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var transaction = Stored("E-TRANSFER FROM A FRIEND", 40, TransactionType.Credit, AccountType.Chequing);
        await factory.SeedAsync(db => db.Transactions.Add(transaction));

        var picked = await (await client.PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description = "Dinner paid back", categoryId = DefaultCategories.Dining, flow = "Refund" })).ReadAsync<TransactionOutputDto>();
        var descriptionOnly = await (await client.PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description = "Dinner paid back by Sam", categoryId = DefaultCategories.Dining })).ReadAsync<TransactionOutputDto>();
        await client.PostAsync("/api/Transaction/ApplyRules", null);

        Assert.Equal((Flow.Refund, true), (picked.Flow, picked.FlowLocked));
        Assert.Equal((Flow.Refund, true), (descriptionOnly.Flow, descriptionOnly.FlowLocked));
        Assert.Equal(Flow.Refund, await factory.QueryAsync(db => db.Transactions.Select(t => t.Flow).SingleAsync()));
    }

    [Fact]
    public async Task Update_TheFlowItAlreadyHas_DoesNotLockIt()
    {
        await using var factory = new FinanceApiFactory();
        var transaction = Stored("METRO", 8, TransactionType.Debit, AccountType.CreditCard);
        await factory.SeedAsync(db => db.Transactions.Add(transaction));

        var updated = await (await factory.CreateClient().PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description = "METRO", categoryId = DefaultCategories.Groceries, flow = "Spending" })).ReadAsync<TransactionOutputDto>();

        Assert.False(updated.FlowLocked);
    }

    [Theory]
    [InlineData("Debit", "Income")]
    [InlineData("Debit", "Refund")]
    [InlineData("Credit", "Spending")]
    public async Task Update_AFlowTheOtherDirection_Returns400(string direction, string flow)
    {
        await using var factory = new FinanceApiFactory();
        var type = Enum.Parse<TransactionType>(direction);
        var transaction = Stored("SOMETHING", 8, type, AccountType.Chequing);
        await factory.SeedAsync(db => db.Transactions.Add(transaction));

        var response = await factory.CreateClient().PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description = "SOMETHING", categoryId = DefaultCategories.Other, flow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("flow", (await response.ReadAsync<HttpValidationProblemDetails>()).Errors.Keys);
        Assert.Equal(TestAccounts.DefaultFlow(AccountType.Chequing, type),
            await factory.QueryAsync(db => db.Transactions.Select(t => t.Flow).SingleAsync()));
    }

    // ---- Reports

    private static FinanceApiFactory FactoryAt(string utcNow) => new(services =>
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse(utcNow)))));

    private static DateTime Day(int month, int day) => new(2026, month, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SpendingReports_CountSpendingMinusRefunds_OnAnyAccount()
    {
        await using var factory = FactoryAt("2026-03-15T17:00:00Z");
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Stored("METRO", 100, TransactionType.Debit, AccountType.Chequing, Day(3, 2)),
            // A refund on chequing reduces spending now, like one on the card.
            Stored("METRO REFUND", 30, TransactionType.Credit, AccountType.Chequing, Day(3, 3), flow: Flow.Refund),
            Stored("PAYMENT THANK YOU", 500, TransactionType.Credit, AccountType.CreditCard, Day(3, 4), flow: Flow.Transfer),
            Stored("TO SAVINGS", 200, TransactionType.Debit, AccountType.Chequing, Day(3, 5), flow: Flow.Transfer),
            Stored("UBER* PENDING", 12, TransactionType.Debit, AccountType.CreditCard, Day(3, 6), flow: Flow.Ignored),
            Stored("PAYROLL", 4000, TransactionType.Credit, AccountType.Chequing, Day(3, 7))));

        var month = await (await factory.CreateClient().GetAsync("/api/Reports/current-month-categorized-graph"))
            .ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal(70m, Assert.Single(month.MonthlyData).Total);
    }

    [Fact]
    public async Task CashFlow_IncomeSpendingNetAndSavedPerMonth_AveragedOverFinishedMonths()
    {
        await using var factory = FactoryAt("2026-03-15T17:00:00Z");
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Stored("LAST YEAR", 999, TransactionType.Credit, AccountType.Chequing, new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc)),
            // January: income 4000, spending 1000 - 100 refunded, 500 into savings.
            Stored("PAYROLL", 4000, TransactionType.Credit, AccountType.Chequing, Day(1, 15)),
            Stored("RENT", 1000, TransactionType.Debit, AccountType.Chequing, Day(1, 1)),
            Stored("REFUND", 100, TransactionType.Credit, AccountType.CreditCard, Day(1, 20)),
            Stored("TO SAVINGS", 500, TransactionType.Debit, AccountType.Chequing, Day(1, 16), flow: Flow.Transfer),
            Stored("FROM CHEQUING", 500, TransactionType.Credit, AccountType.Savings, Day(1, 16)),
            // February: income 4000 + 5 interest, spending 2000, 300 taken out of savings; a pending hold doesn't count.
            Stored("PAYROLL", 4000, TransactionType.Credit, AccountType.Chequing, Day(2, 15)),
            Stored("INTEREST", 5, TransactionType.Credit, AccountType.Savings, Day(2, 28), flow: Flow.Income),
            Stored("TRIP", 2000, TransactionType.Debit, AccountType.CreditCard, Day(2, 10)),
            Stored("TO CHEQUING", 300, TransactionType.Debit, AccountType.Savings, Day(2, 11)),
            Stored("HOLD", 50, TransactionType.Debit, AccountType.Savings, Day(2, 12), flow: Flow.Ignored),
            // March, not over yet: left out of the averages.
            Stored("GROCERIES", 80, TransactionType.Debit, AccountType.CreditCard, Day(3, 2))));

        var report = await (await factory.CreateClient().GetAsync("/api/Reports/cash-flow")).ReadAsync<CashFlowReport>();

        Assert.Equal(
        [
            ("Jan 2026", 4000m, 900m, 3100m, 500m),
            ("Feb 2026", 4005m, 2000m, 2005m, -295m),
            ("Mar 2026", 0m, 80m, -80m, 0m)
        ], report.Months.Select(m => (m.Month, m.Income, m.Spending, m.Net, m.Saved)));
        Assert.Equal((4002.5m, 1450m, 102.5m), (report.AverageMonthlyIncome, report.AverageMonthlySpending, report.AverageMonthlySaved));
    }

    [Fact]
    public async Task CashFlow_WithNothingStored_IsEmpty()
    {
        await using var factory = FactoryAt("2026-03-15T17:00:00Z");

        var report = await (await factory.CreateClient().GetAsync("/api/Reports/cash-flow")).ReadAsync<CashFlowReport>();

        Assert.Empty(report.Months);
        Assert.Equal((0m, 0m, 0m), (report.AverageMonthlyIncome, report.AverageMonthlySpending, report.AverageMonthlySaved));
    }
}

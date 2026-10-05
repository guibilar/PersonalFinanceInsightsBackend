using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

/// <summary>Investment accounts, registered plans and their room, balances, and net worth.</summary>
public class InvestmentTests
{
    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static FinanceApiFactory FactoryOnOctober15() => new(services =>
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-10-15T16:00:00Z")))));

    private static Transaction Tx(Guid accountId, DateTime date, string description, decimal amount, TransactionType type, Flow flow) => new()
    {
        Id = Guid.NewGuid(), Date = date, Description = description, Amount = amount, TransactionType = type, Flow = flow,
        CategoryId = DefaultCategories.Other, AccountId = accountId, Hash = Guid.NewGuid().ToString(),
    };

    private static async Task<AccountOutputDto> AddAsync(HttpClient client, object account)
    {
        var response = await client.PostAsJsonAsync("/api/Accounts", account, FinanceApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<AccountOutputDto>();
    }

    private static async Task<AccountOutputDto> GetAsync(HttpClient client, Guid id) =>
        (await (await client.GetAsync("/api/Accounts")).ReadAsync<List<AccountOutputDto>>()).Single(a => a.Id == id);

    private static async Task<string> RefusalAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return Assert.Single((await response.ReadAsync<HttpValidationProblemDetails>()).Errors[field]);
    }

    [Fact]
    public async Task ATfsa_CountsThisYearsDeposits_AgainstItsRoom_AndWithdrawalsApart()
    {
        await using var factory = FactoryOnOctober15();
        var client = factory.CreateClient();
        var tfsa = await AddAsync(client, new { name = "Wealthsimple TFSA", type = AccountType.Investment, format = StatementFormat.Manual, plan = RegisteredPlan.Tfsa, contributionRoom = 7000m });
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx(tfsa.Id, Day(2025, 12, 20), "Deposit", 1000, TransactionType.Credit, Flow.Transfer),   // last year's
            Tx(tfsa.Id, Day(2026, 2, 1), "Deposit", 2500, TransactionType.Credit, Flow.Transfer),
            Tx(tfsa.Id, Day(2026, 6, 1), "Deposit", 1000, TransactionType.Credit, Flow.Transfer),
            Tx(tfsa.Id, Day(2026, 7, 1), "Withdrawal", 400, TransactionType.Debit, Flow.Transfer),
            Tx(tfsa.Id, Day(2026, 8, 1), "Dividend", 12.34m, TransactionType.Credit, Flow.Income)));

        var account = await GetAsync(client, tfsa.Id);

        Assert.Equal((RegisteredPlan.Tfsa, 7000m, 2026), (account.Plan, account.ContributionRoom, account.ContributionRoomYear));
        Assert.Equal((3500m, 400m, 3500m), (account.DepositedThisYear, account.WithdrawnThisYear, account.RoomLeft));
    }

    [Fact]
    public async Task RoomForAnotherYear_LeavesNoRoomLeft_UntilThisYearsIsEntered()
    {
        await using var factory = FactoryOnOctober15();
        var client = factory.CreateClient();
        var tfsa = await AddAsync(client, new { name = "TFSA", type = AccountType.Investment, plan = RegisteredPlan.Tfsa, contributionRoom = 6500m, contributionRoomYear = 2025 });
        Assert.Null(tfsa.RoomLeft);

        var updated = await (await client.PutAsJsonAsync($"/api/Accounts/{tfsa.Id}", new { name = "TFSA", contributionRoom = 9000m })).ReadAsync<AccountOutputDto>();

        Assert.Equal((9000m, 2026, 9000m), (updated.ContributionRoom, updated.ContributionRoomYear, updated.RoomLeft));
    }

    [Fact]
    public async Task PlansAreForSavingsAndInvestments_RoomNeedsAPlan_AndNoneClearsIt()
    {
        await using var factory = FactoryOnOctober15();
        var client = factory.CreateClient();

        var chequing = await client.PostAsJsonAsync("/api/Accounts", new { name = "Bank", type = AccountType.Chequing, plan = RegisteredPlan.Tfsa }, FinanceApiFactory.Json);
        var noPlan = await client.PostAsJsonAsync("/api/Accounts", new { name = "Brokerage", type = AccountType.Investment, contributionRoom = 100m }, FinanceApiFactory.Json);
        var future = await client.PostAsJsonAsync("/api/Accounts", new { name = "TFSA", type = AccountType.Investment, plan = RegisteredPlan.Tfsa, contributionRoom = 100m, contributionRoomYear = 2027 }, FinanceApiFactory.Json);

        Assert.Contains("savings and investment", await RefusalAsync(chequing, "plan"));
        Assert.Contains("registered plan", await RefusalAsync(noPlan, "contributionRoom"));
        Assert.Contains("2009 to 2026", await RefusalAsync(future, "contributionRoomYear"));

        var savings = await AddAsync(client, new { name = "HISA", type = AccountType.Savings, plan = RegisteredPlan.Tfsa, contributionRoom = 500m });
        // A rename alone leaves the plan as it is.
        var renamed = await (await client.PutAsJsonAsync($"/api/Accounts/{savings.Id}", new { name = "Savings TFSA" })).ReadAsync<AccountOutputDto>();
        var cleared = await (await client.PutAsJsonAsync($"/api/Accounts/{savings.Id}", new { name = "Savings TFSA", plan = RegisteredPlan.None })).ReadAsync<AccountOutputDto>();
        Assert.Equal((RegisteredPlan.Tfsa, 500m), (renamed.Plan, renamed.ContributionRoom));
        Assert.Equal((RegisteredPlan.None, null, null), (cleared.Plan, cleared.ContributionRoom, cleared.ContributionRoomYear));
    }

    [Fact]
    public async Task Balances_AreOneADay_NewestFirst_NotInTheFuture_AndGoWithTheAccount()
    {
        await using var factory = FactoryOnOctober15();
        var client = factory.CreateClient();
        var tfsa = await AddAsync(client, new { name = "TFSA", type = AccountType.Investment });
        var url = $"/api/Accounts/{tfsa.Id}/Balances";

        await client.PostAsJsonAsync(url, new { date = "2026-09-30", amount = 10500.25m });
        await client.PostAsJsonAsync(url, new { date = "2026-10-14", amount = 10800m });
        var replaced = await (await client.PostAsJsonAsync(url, new { date = "2026-10-14", amount = 10750m })).ReadAsync<AccountBalanceOutputDto>();
        var future = await client.PostAsJsonAsync(url, new { date = "2026-10-16", amount = 1m });

        var listed = await (await client.GetAsync(url)).ReadAsync<List<AccountBalanceOutputDto>>();
        Assert.Equal([(Day(2026, 10, 14), 10750m), (Day(2026, 9, 30), 10500.25m)], listed.Select(b => (b.Date, b.Amount)));
        Assert.Equal(listed[0].Id, replaced.Id);
        Assert.Contains("future", await RefusalAsync(future, "date"));
        Assert.Equal((Day(2026, 10, 14), 10750m), ((await GetAsync(client, tfsa.Id)).Balance!.Date, (await GetAsync(client, tfsa.Id)).Balance!.Amount));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{url}/{listed[1].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/Accounts/{tfsa.Id}")).StatusCode);
        Assert.Equal(0, await factory.QueryAsync(db => db.AccountBalances.CountAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task NetWorth_CarriesBalancesThroughTransactions_CardsOwe_AndInvestmentsStartAtTheirFirstBalance()
    {
        await using var factory = FactoryOnOctober15();
        var client = factory.CreateClient();
        var tfsa = await AddAsync(client, new { name = "TFSA", type = AccountType.Investment });
        await factory.SeedAsync(db => db.Transactions.AddRange(
            // Chequing: 1,000 on Sep 30; pay 300 on Aug 10 (before it) and get 2,000 on Oct 1 (after it).
            Tx(Account.DefaultChequingId, Day(2026, 8, 10), "RENT", 300, TransactionType.Debit, Flow.Spending),
            Tx(Account.DefaultChequingId, Day(2026, 10, 1), "PAY", 2000, TransactionType.Credit, Flow.Income),
            Tx(Account.DefaultChequingId, Day(2026, 10, 2), "UBER* PENDING", 50, TransactionType.Debit, Flow.Ignored),
            Tx(Account.DefaultChequingId, Day(2026, 10, 3), "TO TFSA", 500, TransactionType.Debit, Flow.Transfer),
            // Card: owes 200 on Sep 30, then a 50 purchase.
            Tx(Account.DefaultCreditCardId, Day(2026, 10, 5), "METRO", 50, TransactionType.Debit, Flow.Spending),
            // TFSA: worth 5,000 on Sep 30; a deposit after, and a buy that only moves cash into holdings.
            Tx(tfsa.Id, Day(2026, 10, 3), "Deposit", 500, TransactionType.Credit, Flow.Transfer),
            Tx(tfsa.Id, Day(2026, 10, 4), "Buy XEQT", 500, TransactionType.Debit, Flow.Ignored)));
        foreach (var (id, amount) in new[] { (Account.DefaultChequingId, 1000m), (Account.DefaultCreditCardId, 200m), (tfsa.Id, 5000m) })
        {
            await client.PostAsJsonAsync($"/api/Accounts/{id}/Balances", new { date = "2026-09-30", amount });
        }

        var report = await (await client.GetAsync("/api/Reports/net-worth")).ReadAsync<NetWorthReport>();

        var byName = report.Accounts.ToDictionary(a => a.Name);
        Assert.Equal((2500m, true), (byName["Chequing"].Balance!.Value, byName["Chequing"].MovedSince));
        Assert.Equal((250m, true), (byName["Credit card"].Balance!.Value, byName["Credit card"].IsLiability));
        Assert.Equal(5500m, byName["TFSA"].Balance);
        Assert.Null(byName["Savings"].Balance);
        Assert.Equal((8000m, 250m, 7750m), (report.Assets, report.Liabilities, report.NetWorth));

        // August: only the chequing account is known (back from its balance through the rent); the TFSA starts in September.
        Assert.Equal(
        [
            ("Aug 2026", 1000m, 0m, 1),
            ("Sep 2026", 6000m, 200m, 3),
            ("Oct 2026", 8000m, 250m, 3),
        ], report.Months.Select(m => (m.Month, m.Assets, m.Liabilities, m.AccountsCounted)));
        Assert.Equal(Day(2026, 10, 15), report.Months[^1].Date);
    }

    [Fact]
    public async Task InvestmentMovements_AreTransfers_KeepTheirCategory_AndCountAsSavingsInTheCashFlow()
    {
        await using var factory = FactoryOnOctober15();
        var client = factory.CreateClient();
        var tfsa = await AddAsync(client, new { name = "TFSA", type = AccountType.Investment, format = StatementFormat.Manual });

        var added = await (await client.PostAsJsonAsync("/api/Transaction", new
        {
            date = "2026-10-03", description = "Deposit", amount = 500m, transactionType = TransactionType.Credit, accountId = tfsa.Id,
        }, FinanceApiFactory.Json)).ReadAsync<TransactionOutputDto>();
        await client.PostAsync("/api/Transaction/ApplyRules", null);
        var cashFlow = await (await client.GetAsync("/api/Reports/cash-flow")).ReadAsync<CashFlowReport>();

        Assert.Equal((Flow.Transfer, DefaultCategories.Other), (added.Flow, added.Category.Id));
        Assert.Equal(DefaultCategories.Other, await factory.QueryAsync(db => db.Transactions.Where(t => t.Id == added.Id).Select(t => t.CategoryId).SingleAsync()));
        Assert.Equal(500m, cashFlow.Months.Single(m => m.Month == "Oct 2026").SavingsChange);
    }
}

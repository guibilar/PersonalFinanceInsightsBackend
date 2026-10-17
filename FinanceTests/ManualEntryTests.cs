using System.Net;
using System.Net.Http.Json;
using System.Text;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

/// <summary>Adding transactions by hand, for accounts whose bank offers no statement file.</summary>
public class ManualEntryTests
{
    private static async Task<Guid> AddManualAccountAsync(HttpClient client, AccountType type = AccountType.Chequing)
    {
        var response = await client.PostAsJsonAsync("/api/Accounts",
            new { name = "Neo chequing", type, format = StatementFormat.Manual }, FinanceApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.ReadAsync<AccountOutputDto>();
        Assert.Equal(StatementFormat.Manual, account.Format);
        return account.Id;
    }

    private static object Tx(Guid accountId, string description = "FARMERS FRIDGE", string type = "Debit",
        Guid? categoryId = null, object? flow = null, bool allowDuplicate = false) => new
    {
        date = "2026-09-10", description, amount = 12.5m, transactionType = type, accountId, categoryId, flow, allowDuplicate
    };

    [Fact]
    public async Task Import_IntoAManualAccount_IsRefusedWithAClearReason()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("2026-01-03,METRO,8.00,")), "file", "s.csv" },
            { new StringContent(accountId.ToString()), "accountId" }
        };

        var response = await client.PostAsync("/api/Transaction/Import", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("add its transactions by hand", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WithoutACategoryOrFlow_TheRulesDecide_AndTheRulesStayInCharge()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);
        await client.PostAsJsonAsync("/api/CategoryRules", new { keyword = "FARMERS FRIDGE", categoryId = DefaultCategories.Groceries, position = 0 }, FinanceApiFactory.Json);

        var response = await client.PostAsJsonAsync("/api/Transaction", Tx(accountId), FinanceApiFactory.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await factory.QueryAsync(db => db.Transactions.SingleAsync());
        Assert.Equal(DefaultCategories.Groceries, stored.CategoryId);
        Assert.False(stored.CategoryLocked);
        Assert.Equal(Flow.Spending, stored.Flow);
        Assert.False(stored.FlowLocked);
    }

    [Fact]
    public async Task APickedCategoryAndFlow_AreKept_WhenTheRulesAreReapplied()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);
        await client.PostAsJsonAsync("/api/CategoryRules", new { keyword = "FARMERS FRIDGE", categoryId = DefaultCategories.Groceries, position = 0 }, FinanceApiFactory.Json);

        var response = await client.PostAsJsonAsync("/api/Transaction",
            Tx(accountId, categoryId: DefaultCategories.Dining, flow: "Transfer"), FinanceApiFactory.Json);
        await client.PostAsync("/api/Transaction/ApplyRules", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await factory.QueryAsync(db => db.Transactions.SingleAsync());
        Assert.Equal((DefaultCategories.Dining, true, Flow.Transfer, true),
            (stored.CategoryId, stored.CategoryLocked, stored.Flow, stored.FlowLocked));
    }

    [Theory]
    [InlineData("Debit", "Income")]
    [InlineData("Credit", "Spending")]
    public async Task AFlowThatDoesNotFitTheDirection_IsRefused(string type, string flow)
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);

        var response = await client.PostAsJsonAsync("/api/Transaction", Tx(accountId, type: type, flow: flow), FinanceApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await factory.QueryAsync(db => db.Transactions.ToListAsync()));
    }

    [Fact]
    public async Task MoneyIn_OnAManualChequingAccount_IsIncome()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);

        await client.PostAsJsonAsync("/api/Transaction", Tx(accountId, "PAYROLL", "Credit"), FinanceApiFactory.Json);

        Assert.Equal(Flow.Income, (await factory.QueryAsync(db => db.Transactions.SingleAsync())).Flow);
    }

    [Fact]
    public async Task AnIdenticalTransaction_IsAConflict_UnlessDuplicatesAreAllowed()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);

        var first = await client.PostAsJsonAsync("/api/Transaction", Tx(accountId), FinanceApiFactory.Json);
        var refused = await client.PostAsJsonAsync("/api/Transaction", Tx(accountId), FinanceApiFactory.Json);
        var second = await client.PostAsJsonAsync("/api/Transaction", Tx(accountId, allowDuplicate: true), FinanceApiFactory.Json);
        var third = await client.PostAsJsonAsync("/api/Transaction", Tx(accountId, allowDuplicate: true), FinanceApiFactory.Json);

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.Created, HttpStatusCode.Created],
            [first.StatusCode, refused.StatusCode, second.StatusCode, third.StatusCode]);
        var hashes = await factory.QueryAsync(db => db.Transactions.Select(t => t.Hash).ToListAsync());
        Assert.Equal(3, hashes.Distinct().Count());
    }

    [Fact]
    public async Task TheDescription_IsStoredWithoutSurroundingSpaces()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);

        var response = await client.PostAsJsonAsync("/api/Transaction", Tx(accountId, "  FARMERS FRIDGE  "), FinanceApiFactory.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("FARMERS FRIDGE", (await response.ReadAsync<TransactionOutputDto>()).Description);
        Assert.Equal("FARMERS FRIDGE", await factory.QueryAsync(db => db.Transactions.Select(t => t.Description).SingleAsync()));
    }

    [Theory]
    [InlineData("/api/Transaction")]
    [InlineData("/api/Budget")]
    public async Task AnAmountWithAFractionOfACent_Returns400(string endpoint)
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);
        object body = endpoint == "/api/Budget"
            ? new { name = "Food", description = "d", status = "Active", amount = 100.005m }
            : new { date = "2026-09-10", description = "COFFEE", amount = 1.005m, transactionType = "Debit", accountId };

        var response = await client.PostAsJsonAsync(endpoint, body, FinanceApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("at most 2 decimal places", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/Transaction")]
    [InlineData("/api/Budget")]
    public async Task AnAmountOverABillion_Returns400(string endpoint)
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);
        // Two of these would overflow every total they are added to.
        const decimal huge = 79228162514264337593543950335m;
        object body = endpoint == "/api/Budget"
            ? new { name = "Food", description = "d", status = "Active", amount = huge }
            : new { date = "2026-09-10", description = "COFFEE", amount = huge, transactionType = "Debit", accountId };

        var response = await client.PostAsJsonAsync(endpoint, body, FinanceApiFactory.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("9999-12-31")]  // past the calendar's end, the breakdown failed with a 500
    [InlineData("2028-01-01")]  // more than a year ahead
    [InlineData("0206-09-10")]  // years of periods: the breakdown refused every request without a start date
    [InlineData("1999-12-31")]
    public async Task ADateWithAMistypedYear_Returns400_AndTheBreakdownStillWorks(string date)
    {
        await using var factory = new FinanceApiFactory(services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-10-15T16:00:00Z")))));
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);
        await client.PostAsJsonAsync("/api/Transaction", Tx(accountId), FinanceApiFactory.Json);

        var response = await client.PostAsJsonAsync("/api/Transaction",
            new { date, description = "COFFEE", amount = 1m, transactionType = "Debit", accountId }, FinanceApiFactory.Json);
        var breakdown = await client.PostAsJsonAsync("/api/Reports/breakdown", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Choose a date from 2000 to 2027-10-15", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, breakdown.StatusCode);
        Assert.Equal(1, (await breakdown.ReadAsync<TransactionBreakdown>()).Count);
    }

    [Theory]
    [InlineData("2000-01-01")]
    [InlineData("2027-10-15")]
    public async Task ADateFrom2000ToAYearAhead_IsAdded(string date)
    {
        await using var factory = new FinanceApiFactory(services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-10-15T16:00:00Z")))));
        var client = factory.CreateClient();
        var accountId = await AddManualAccountAsync(client);

        var response = await client.PostAsJsonAsync("/api/Transaction",
            new { date, description = "COFFEE", amount = 1m, transactionType = "Debit", accountId }, FinanceApiFactory.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}

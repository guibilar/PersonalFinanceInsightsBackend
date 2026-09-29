using System.Net;
using System.Net.Http.Json;
using System.Text;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

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
        object? category = null, object? flow = null, bool allowDuplicate = false) => new
    {
        date = "2026-09-10", description, amount = 12.5m, transactionType = type, accountId, category, flow, allowDuplicate
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
        await client.PostAsJsonAsync("/api/CategoryRules", new { keyword = "FARMERS FRIDGE", category = "Groceries", position = 0 }, FinanceApiFactory.Json);

        var response = await client.PostAsJsonAsync("/api/Transaction", Tx(accountId), FinanceApiFactory.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await factory.QueryAsync(db => db.Transactions.SingleAsync());
        Assert.Equal(Category.Groceries, stored.Category);
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
        await client.PostAsJsonAsync("/api/CategoryRules", new { keyword = "FARMERS FRIDGE", category = "Groceries", position = 0 }, FinanceApiFactory.Json);

        var response = await client.PostAsJsonAsync("/api/Transaction",
            Tx(accountId, category: "Dining", flow: "Transfer"), FinanceApiFactory.Json);
        await client.PostAsync("/api/Transaction/ApplyRules", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = await factory.QueryAsync(db => db.Transactions.SingleAsync());
        Assert.Equal((Category.Dining, true, Flow.Transfer, true),
            (stored.Category, stored.CategoryLocked, stored.Flow, stored.FlowLocked));
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
}

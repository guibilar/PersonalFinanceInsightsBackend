using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceTests;

/// <summary>Accounts as data: the defaults every database starts with, adding, renaming and deleting.</summary>
public class AccountTests
{
    private const string Accounts = "/api/Accounts";

    private static async Task<List<AccountOutputDto>> GetAccountsAsync(HttpClient client) =>
        await (await client.GetAsync(Accounts)).ReadAsync<List<AccountOutputDto>>();

    [Fact]
    public async Task EveryDatabase_StartsWithOneAccountPerKind()
    {
        await using var factory = new FinanceApiFactory();

        var accounts = await GetAccountsAsync(factory.CreateClient());

        Assert.Equal(
        [
            (Account.DefaultChequingId, "Chequing", AccountType.Chequing, 0),
            (Account.DefaultSavingsId, "Savings", AccountType.Savings, 0),
            (Account.DefaultCreditCardId, "Credit card", AccountType.CreditCard, 0)
        ], accounts.Select(a => (a.Id, a.Name, a.Type, a.TransactionCount)));
    }

    [Fact]
    public async Task Add_TrimsTheName_AndListsItWithItsKind()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Accounts, new { name = "  Amex  ", type = "CreditCard" });
        var accounts = await GetAccountsAsync(client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<AccountOutputDto>();
        Assert.Equal(("Amex", AccountType.CreditCard, 0), (created.Name, created.Type, created.TransactionCount));
        // By kind (in the enum's order), then name.
        Assert.Equal(["Chequing", "Savings", "Amex", "Credit card"], accounts.Select(a => a.Name));
    }

    [Theory]
    [InlineData("", "CreditCard")]
    [InlineData("Visa", null)]
    [InlineData("Visa", "Crypto")]
    public async Task Add_InvalidValues_Return400(string name, string? type)
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(Accounts, new { name, type });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NameTaken_IgnoringCase_Returns409_OnAddAndRename()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var added = await client.PostAsJsonAsync(Accounts, new { name = "credit CARD", type = "CreditCard" });
        var renamed = await client.PutAsJsonAsync($"{Accounts}/{Account.DefaultSavingsId}", new { name = "CHEQUING" });
        // Changing only the case of its own name is fine.
        var ownName = await client.PutAsJsonAsync($"{Accounts}/{Account.DefaultSavingsId}", new { name = "savings" });

        Assert.Equal(HttpStatusCode.Conflict, added.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownName.StatusCode);
    }

    [Fact]
    public async Task Rename_ShowsInTheTransactions_AndKeepsTheKind()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-01-03,METRO,8.00,"));

        var renamed = await (await client.PutAsJsonAsync($"{Accounts}/{Account.DefaultCreditCardId}",
            new { name = "Visa", type = "Chequing" })).ReadAsync<AccountOutputDto>();
        var listed = await (await client.PostAsJsonAsync("/api/Transaction/Filter", new { }))
            .ReadAsync<Finance.Domain.Common.PaginatedList<TransactionOutputDto>>();

        Assert.Equal(("Visa", AccountType.CreditCard, 1), (renamed.Name, renamed.Type, renamed.TransactionCount));
        Assert.Equal("Visa", Assert.Single(listed.Items).AccountName);
    }

    [Fact]
    public async Task Delete_AnEmptyAccount_Works_ButNotOneWithTransactions_EvenDeletedOnes()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-01-03,METRO,8.00,"));
        var id = await factory.QueryAsync(db => db.Transactions.Select(t => t.Id).SingleAsync());
        await client.DeleteAsync($"/api/Transaction/{id}");

        var withDeletedRow = await client.DeleteAsync($"{Accounts}/{Account.DefaultCreditCardId}");
        var empty = await client.DeleteAsync($"{Accounts}/{Account.DefaultSavingsId}");
        var unknown = await client.DeleteAsync($"{Accounts}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Conflict, withDeletedRow.StatusCode);
        Assert.Contains("only an empty account can be deleted", (await withDeletedRow.ReadAsync<ProblemDetails>()).Detail);
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(["Chequing", "Credit card"], (await GetAccountsAsync(client)).Select(a => a.Name));
    }

    [Fact]
    public async Task TransactionCount_IncludesDeletedTransactions()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-01-03,METRO,8.00,\n2026-01-04,SHELL,40.00,"));
        var id = await factory.QueryAsync(db => db.Transactions.Select(t => t.Id).FirstAsync());
        await client.DeleteAsync($"/api/Transaction/{id}");

        var accounts = await GetAccountsAsync(client);
        var card = accounts.Single(a => a.Id == Account.DefaultCreditCardId);

        Assert.Equal(2, card.TransactionCount);
        // How far its statements reach: the latest row, deleted or not; nothing for an account without any.
        Assert.Equal(new DateTime(2026, 1, 4), card.LastTransactionDate);
        Assert.Null(accounts.Single(a => a.Id == Account.DefaultChequingId).LastTransactionDate);
    }

    [Fact]
    public async Task Filter_ByAccountAndFlow()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("""
            2026-01-03,METRO,8.00,
            2026-01-05,PAYMENT THANK YOU,,500.00
            """));
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-01-04,SHELL,40.00,", "Chequing"));

        async Task<List<string>> DescriptionsAsync(object filter) =>
            (await (await client.PostAsJsonAsync("/api/Transaction/Filter", filter, FinanceApiFactory.Json))
                .ReadAsync<Finance.Domain.Common.PaginatedList<TransactionOutputDto>>()).Items.Select(t => t.Description).ToList();

        Assert.Equal(["PAYMENT THANK YOU", "METRO"], await DescriptionsAsync(new { accountId = Account.DefaultCreditCardId }));
        Assert.Equal(["SHELL", "METRO"], await DescriptionsAsync(new { flow = Flow.Spending }));
        Assert.Equal(["PAYMENT THANK YOU"], await DescriptionsAsync(new { accountId = Account.DefaultCreditCardId, flow = Flow.Transfer }));
    }
}

using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceTests;

/// <summary>Duplicate detection, budget totals and input validation.</summary>
public class DataIntegrityTests
{
    private static bool OnPostgres => TestDatabase.OnPostgres;

    private static object ManualTransaction(
        AccountType account = AccountType.CreditCard, string? transactionType = "Debit", string? category = "Dining",
        decimal amount = 16.99m, string? date = "2026-09-10", int? flow = null) => new
    {
        date, description = "NETFLIX", amount, category, transactionType, flow, accountId = TestAccounts.Id(account)
    };

    // ---- Duplicates

    [Fact]
    public async Task Import_SamePurchaseOnAnotherAccount_IsNotADuplicate()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        const string csv = "2026-01-03,NETFLIX.COM,16.99,";

        var creditCard = await (await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(csv, "CreditCard")))
            .ReadAsync<ImportResultDto>();
        var savings = await (await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(csv, "Savings")))
            .ReadAsync<ImportResultDto>();
        var savingsAgain = await (await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(csv, "Savings")))
            .ReadAsync<ImportResultDto>();

        Assert.Equal(1, creditCard.Added);
        Assert.Equal(1, savings.Added);
        Assert.Equal((0, 1), (savingsAgain.Added, savingsAgain.Skipped));
        var accounts = await factory.QueryAsync(db => db.Transactions.Select(t => t.AccountId).ToListAsync());
        Assert.Equal(2, accounts.Count);
        Assert.Equal([Account.DefaultCreditCardId, Account.DefaultSavingsId], accounts.ToHashSet());
    }

    [Fact]
    public async Task CreateTransaction_DuplicateIsPerAccount()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/Transaction", ManualTransaction());
        var otherAccount = await client.PostAsJsonAsync("/api/Transaction", ManualTransaction(account: AccountType.Chequing));
        var duplicate = await client.PostAsJsonAsync("/api/Transaction", ManualTransaction());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherAccount.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Import_ConcurrentUploadsOfTheSameStatement_StoreEachRowOnce()
    {
        // Only PostgreSQL enforces the unique index that settles the race.
        Assert.SkipUnless(OnPostgres, "Needs PostgreSQL: set FINANCE_TEST_POSTGRES (e.g. to \"container\").");
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var csv = string.Join('\n', Enumerable.Range(1, 150).Select(i => $"2026-01-{1 + i % 28:00},SHOP {i},{i}.00,"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => client.PostAsync("/api/Transaction/Import", ImportTests.Statement(csv))));
        var results = await Task.WhenAll(responses.Select(r => r.ReadAsync<ImportResultDto>()));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(150, results.Sum(r => r.Added));
        Assert.Equal(150, await factory.QueryAsync(db => db.Transactions.CountAsync()));
    }

    [Fact]
    public async Task Migration_RemovesExistingDuplicates_KeepingTheBudgetLinkedCopy()
    {
        Assert.SkipUnless(OnPostgres, "Needs PostgreSQL: set FINANCE_TEST_POSTGRES (e.g. to \"container\").");
        await using var factory = new FinanceApiFactory();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("StoreEnumsAsStrings");

        var budgetId = Guid.NewGuid();
        var linkedCopy = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("""INSERT INTO "Budgets" ("Id", "Name", "Description", "Status", "Amount") VALUES ({0}, 'Trip', 'd', 'Active', 100)""", budgetId);
        foreach (var (id, account, linked) in new[]
                 {
                     (Guid.NewGuid(), "CreditCard", false), (linkedCopy, "CreditCard", true), (Guid.NewGuid(), "CreditCard", false),
                     (Guid.NewGuid(), "Chequing", false)
                 })
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Transactions" ("Id", "Date", "Description", "Amount", "Category", "TransactionType", "Account", "Hash", "BudgetId")
                VALUES ({0}, '2026-01-03', 'HOTEL', 80, 'Other', 'Debit', {1}, 'same-hash', {2})
                """, id, account, linked ? budgetId : null!);
        }

        await migrator.MigrateAsync();

        // Later migrations move them onto the default accounts.
        var remaining = await db.Transactions.AsNoTracking().ToDictionaryAsync(t => t.AccountId, t => t.Id);
        Assert.Equal(2, remaining.Count);
        Assert.Equal(linkedCopy, remaining[Account.DefaultCreditCardId]);
        Assert.True(remaining.ContainsKey(Account.DefaultChequingId));
    }

    // ---- Budget totals

    [Fact]
    public async Task Budget_LinkedRefundReducesSpent()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var budgetId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = budgetId, Name = "Trip", Description = "d", Amount = 100, Status = Status.Active });
            foreach (var (amount, type) in new[] { (60m, TransactionType.Debit), (20m, TransactionType.Credit) })
            {
                db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(), Date = DateTime.UtcNow, Description = $"HOTEL {type}", Amount = amount,
                    TransactionType = type, AccountId = Account.DefaultCreditCardId, Hash = Guid.NewGuid().ToString(), BudgetId = budgetId
                });
            }
        });

        var budget = await (await client.GetAsync($"/api/Budget/{budgetId}")).ReadAsync<BudgetOutputDto>();

        Assert.Equal((40m, 60m, 40m), (budget.TotalSpent, budget.TotalRemaining, budget.PercentageUsed));
    }

    // ---- Validation

    public static TheoryData<object> InvalidTransactions => new()
    {
        ManualTransaction(transactionType: null),
        ManualTransaction(flow: 99),
        ManualTransaction(date: null),
        ManualTransaction(amount: 0),
        ManualTransaction(amount: -5),
        new { date = "2026-09-10", description = "NETFLIX", amount = 16.99, category = 99, transactionType = "Debit", accountId = Account.DefaultCreditCardId },
        new { date = "2026-09-10", description = "NETFLIX", amount = 16.99, category = "Dining", transactionType = "Debit" },
    };

    [Theory]
    [MemberData(nameof(InvalidTransactions))]
    public async Task CreateTransaction_MissingOrInvalidValues_Return400(object transaction)
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/Transaction", transaction);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.QueryAsync(db => db.Transactions.CountAsync()));
    }
}

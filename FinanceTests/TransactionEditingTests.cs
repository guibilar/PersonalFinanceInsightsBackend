using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinanceTests;

/// <summary>Correcting transactions by hand and re-applying the category rules.</summary>
public class TransactionEditingTests
{
    private static Transaction Stored(string description, Guid category, AccountType account = AccountType.CreditCard,
        bool locked = false) => new()
    {
        Id = Guid.NewGuid(), Date = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), Description = description, Amount = 10,
        CategoryId = category, TransactionType = TransactionType.Debit, AccountId = TestAccounts.Id(account),
        Flow = TestAccounts.DefaultFlow(account, TransactionType.Debit), Hash = Guid.NewGuid().ToString(),
        CategoryLocked = locked
    };

    [Fact]
    public async Task Update_CorrectsAndLocksTheCategory_AndReimportStillRecognisesTheRow()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        const string csv = "2026-01-03,SQ *CORNER STORE,12.00,";
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(csv));
        var id = await factory.QueryAsync(db => db.Transactions.Select(t => t.Id).SingleAsync());

        var response = await client.PutAsJsonAsync($"/api/Transaction/{id}",
            new { description = "  Corner store (snacks) ", categoryId = DefaultCategories.Groceries }, FinanceApiFactory.Json);
        var reimport = await (await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(csv))).ReadAsync<ImportResultDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<TransactionOutputDto>();
        Assert.Equal(("Corner store (snacks)", DefaultCategories.Groceries, true), (updated.Description, updated.Category.Id, updated.CategoryLocked));
        Assert.Equal((0, 1), (reimport.Added, reimport.Skipped));
    }

    [Fact]
    public async Task Update_OnlyADescriptionFix_LeavesTheCategoryToTheRules()
    {
        await using var factory = new FinanceApiFactory();
        var transaction = Stored("WAL-MART #3059", DefaultCategories.Other);
        await factory.SeedAsync(db => db.Transactions.Add(transaction));
        var client = factory.CreateClient();

        var updated = await (await client.PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description = "WAL-MART #3059 (groceries run)", categoryId = DefaultCategories.Other })).ReadAsync<TransactionOutputDto>();
        await client.PostAsync("/api/Transaction/ApplyRules", null);

        Assert.False(updated.CategoryLocked);
        Assert.Equal(DefaultCategories.Groceries, await factory.QueryAsync(db => db.Transactions.Select(t => t.CategoryId).SingleAsync()));
    }

    [Fact]
    public async Task Update_KeepsTheBudgetInTheResponse()
    {
        await using var factory = new FinanceApiFactory();
        var budgetId = Guid.NewGuid();
        var transaction = Stored("HOTEL", DefaultCategories.Other);
        transaction.BudgetId = budgetId;
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = budgetId, Name = "Trip", Description = "d", Amount = 100, Status = Status.Active });
            db.Transactions.Add(transaction);
        });

        var updated = await (await factory.CreateClient().PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description = "Hotel", categoryId = DefaultCategories.Entertainment })).ReadAsync<TransactionOutputDto>();

        Assert.Equal("Trip", updated.Budget?.Name);
    }

    [Theory]
    [InlineData("   ", "Dining")]
    [InlineData("Lunch", null)]
    [InlineData("Lunch", "Travel")]
    public async Task Update_InvalidValues_Return400(string description, string? category)
    {
        await using var factory = new FinanceApiFactory();
        var transaction = Stored("LUNCH", DefaultCategories.Other);
        await factory.SeedAsync(db => db.Transactions.Add(transaction));

        var response = await factory.CreateClient().PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description, categoryId = category is null ? (Guid?)null : DefaultCategories.IdOf(category) ?? Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Unknown_Returns404()
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PutAsJsonAsync($"/api/Transaction/{Guid.NewGuid()}",
            new { description = "Lunch", categoryId = DefaultCategories.Dining });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApplyRules_RecategorisesOnlyRuleManagedTransactions()
    {
        await using var factory = new FinanceApiFactory();
        var stale = Stored("WAL-MART #3059", DefaultCategories.Other);          // rules now say Groceries
        var alreadyRight = Stored("TIM HORTONS #55", DefaultCategories.Dining);  // unchanged
        var handPicked = Stored("METRO #123", DefaultCategories.Pet, locked: true);
        var savings = Stored("METRO TRANSFER", DefaultCategories.Other, AccountType.Savings);
        // Rent generated before real payments were imported: the migration locked its category.
        var fixedRent = Stored("Monthly Rent", DefaultCategories.Rent, AccountType.Chequing, locked: true);
        await factory.SeedAsync(db => db.Transactions.AddRange(stale, alreadyRight, handPicked, savings, fixedRent));

        var result = await (await factory.CreateClient().PostAsync("/api/Transaction/ApplyRules", null))
            .ReadAsync<RulesResultDto>();

        // Every one of them has its flow checked; none of the flows changes.
        Assert.Equal((5, 1), (result.Checked, result.Updated));
        var categories = await factory.QueryAsync(db => db.Transactions.ToDictionaryAsync(t => t.Id, t => t.CategoryId));
        Assert.Equal(DefaultCategories.Groceries, categories[stale.Id]);
        Assert.Equal(DefaultCategories.Dining, categories[alreadyRight.Id]);
        Assert.Equal(DefaultCategories.Pet, categories[handPicked.Id]);
        Assert.Equal(DefaultCategories.Other, categories[savings.Id]);
        Assert.Equal(DefaultCategories.Rent, categories[fixedRent.Id]);
    }
}

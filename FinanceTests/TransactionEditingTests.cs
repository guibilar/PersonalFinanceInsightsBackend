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
    private static Transaction Stored(string description, Category category, AccountType account = AccountType.CreditCard,
        bool locked = false) => new()
    {
        Id = Guid.NewGuid(), Date = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), Description = description, Amount = 10,
        Category = category, TransactionType = TransactionType.Debit, Account = account, Hash = Guid.NewGuid().ToString(),
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
            new { description = "  Corner store (snacks) ", category = "Groceries" }, FinanceApiFactory.Json);
        var reimport = await (await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(csv))).ReadAsync<ImportResultDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<TransactionOutputDto>();
        Assert.Equal(("Corner store (snacks)", Category.Groceries, true), (updated.Description, updated.Category, updated.CategoryLocked));
        Assert.Equal((0, 1), (reimport.Added, reimport.Skipped));
    }

    [Fact]
    public async Task Update_OnlyADescriptionFix_LeavesTheCategoryToTheRules()
    {
        await using var factory = new FinanceApiFactory();
        var transaction = Stored("WAL-MART #3059", Category.Other);
        await factory.SeedAsync(db => db.Transactions.Add(transaction));
        var client = factory.CreateClient();

        var updated = await (await client.PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description = "WAL-MART #3059 (groceries run)", category = "Other" })).ReadAsync<TransactionOutputDto>();
        await client.PostAsync("/api/Transaction/ApplyCategoryRules", null);

        Assert.False(updated.CategoryLocked);
        Assert.Equal(Category.Groceries, await factory.QueryAsync(db => db.Transactions.Select(t => t.Category).SingleAsync()));
    }

    [Fact]
    public async Task Update_KeepsTheBudgetInTheResponse()
    {
        await using var factory = new FinanceApiFactory();
        var budgetId = Guid.NewGuid();
        var transaction = Stored("HOTEL", Category.Other);
        transaction.BudgetId = budgetId;
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = budgetId, Name = "Trip", Description = "d", Amount = 100, Status = Status.Active });
            db.Transactions.Add(transaction);
        });

        var updated = await (await factory.CreateClient().PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description = "Hotel", category = "Entertainment" })).ReadAsync<TransactionOutputDto>();

        Assert.Equal("Trip", updated.Budget?.Name);
    }

    [Theory]
    [InlineData("   ", "Dining")]
    [InlineData("Lunch", null)]
    [InlineData("Lunch", "Travel")]
    public async Task Update_InvalidValues_Return400(string description, string? category)
    {
        await using var factory = new FinanceApiFactory();
        var transaction = Stored("LUNCH", Category.Other);
        await factory.SeedAsync(db => db.Transactions.Add(transaction));

        var response = await factory.CreateClient().PutAsJsonAsync($"/api/Transaction/{transaction.Id}", new { description, category });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Unknown_Returns404()
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PutAsJsonAsync($"/api/Transaction/{Guid.NewGuid()}",
            new { description = "Lunch", category = "Dining" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApplyCategoryRules_RecategorisesOnlyRuleManagedTransactions()
    {
        await using var factory = new FinanceApiFactory();
        var stale = Stored("WAL-MART #3059", Category.Other);          // rules now say Groceries
        var alreadyRight = Stored("TIM HORTONS #55", Category.Dining);  // unchanged
        var handPicked = Stored("METRO #123", Category.Pet, locked: true);
        var savings = Stored("METRO TRANSFER", Category.Other, AccountType.Savings);
        // Rent generated before real payments were imported: the migration locked its category.
        var fixedRent = Stored("Monthly Rent", Category.Rent, AccountType.Chequing, locked: true);
        await factory.SeedAsync(db => db.Transactions.AddRange(stale, alreadyRight, handPicked, savings, fixedRent));

        var result = await (await factory.CreateClient().PostAsync("/api/Transaction/ApplyCategoryRules", null))
            .ReadAsync<CategoryRulesResultDto>();

        Assert.Equal((2, 1), (result.Checked, result.Updated));
        var categories = await factory.QueryAsync(db => db.Transactions.ToDictionaryAsync(t => t.Id, t => t.Category));
        Assert.Equal(Category.Groceries, categories[stale.Id]);
        Assert.Equal(Category.Dining, categories[alreadyRight.Id]);
        Assert.Equal(Category.Pet, categories[handPicked.Id]);
        Assert.Equal(Category.Other, categories[savings.Id]);
        Assert.Equal(Category.Rent, categories[fixedRent.Id]);
    }
}

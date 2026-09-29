using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceTests;

/// <summary>Categories as data: added, renamed, recoloured and deleted, with everything that uses them following.</summary>
public class CategoryTests
{
    private const string Categories = "/api/Categories";

    private static async Task<List<CategoryOutputDto>> ListAsync(HttpClient client) =>
        await (await client.GetAsync(Categories)).ReadAsync<List<CategoryOutputDto>>();

    private static async Task<CategoryOutputDto> AddAsync(HttpClient client, string name, string color = "#123456")
    {
        var response = await client.PostAsJsonAsync(Categories, new { name, color });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<CategoryOutputDto>();
    }

    private static Transaction Stored(string description, Guid categoryId, bool hidden = false) => new()
    {
        Id = Guid.NewGuid(), Date = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc), Description = description, Amount = 10,
        CategoryId = categoryId, TransactionType = TransactionType.Debit, Flow = Flow.Spending, Hidden = hidden,
        AccountId = Account.DefaultCreditCardId, Hash = Guid.NewGuid().ToString()
    };

    // ---- Listing

    [Fact]
    public async Task EveryDatabase_StartsWithTheCategoriesThereWere_ByName_WithTheirColours()
    {
        await using var factory = new FinanceApiFactory();

        var categories = await ListAsync(factory.CreateClient());

        Assert.Equal(
            ["Dining", "Entertainment", "Groceries", "Health", "Other", "Pet", "Rent", "Shopping", "Subscriptions", "Transportation", "Utilities"],
            categories.Select(c => c.Name));
        Assert.Equal("#1F76B5", categories.Single(c => c.Name == "Dining").Color);
        // The rules shipped in import-rules.json use them.
        Assert.True(categories.Single(c => c.Name == "Groceries").RuleCount > 0);
        // Only the two the app relies on are built in.
        Assert.Equal(["Other", "Rent"], categories.Where(c => c.BuiltInReason is not null).Select(c => c.Name));
        Assert.Equal("Other", categories.Single(c => c.IsFallback).Name);
    }

    [Fact]
    public async Task TheList_CountsTransactions_DeletedOnesIncluded()
    {
        await using var factory = new FinanceApiFactory();
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Stored("PIZZA", DefaultCategories.Dining), Stored("SUSHI", DefaultCategories.Dining, hidden: true)));

        var dining = (await ListAsync(factory.CreateClient())).Single(c => c.Id == DefaultCategories.Dining);

        Assert.Equal(2, dining.TransactionCount);
    }

    // ---- Adding and editing

    [Fact]
    public async Task Add_TrimsTheName_AndStoresTheColourUpperCase()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var coffee = await AddAsync(client, "  Coffee ", "#a1b2c3");

        Assert.Equal(("Coffee", "#A1B2C3", 0, 0, null), (coffee.Name, coffee.Color, coffee.TransactionCount, coffee.RuleCount, coffee.BuiltInReason));
        Assert.Contains(await ListAsync(client), c => c.Id == coffee.Id && c.Name == "Coffee");
    }

    [Theory]
    [InlineData("   ", "#123456")]
    [InlineData("Coffee", "123456")]
    [InlineData("Coffee", "#12345G")]
    [InlineData("Coffee", "red")]
    [InlineData("Coffee", null)]
    public async Task Add_InvalidValues_Return400(string name, string? color)
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(Categories, new { name, color });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Names_AreUniqueIgnoringCase_AndAtMost50Characters()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var taken = await client.PostAsJsonAsync(Categories, new { name = "groceries", color = "#123456" });
        var atLimit = await client.PostAsJsonAsync(Categories, new { name = new string('c', 50), color = "#123456" });
        var tooLong = await client.PostAsJsonAsync(Categories, new { name = new string('c', 51), color = "#123456" });

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Contains("A category named \\\"groceries\\\" already exists.", await taken.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task RenamingOrRecolouring_ShowsEverywhereTheCategoryIsUsed()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await factory.SeedAsync(db => db.Transactions.Add(Stored("SAFEWAY", DefaultCategories.Groceries)));

        var response = await client.PutAsJsonAsync($"{Categories}/{DefaultCategories.Groceries}", new { name = "Food", color = "#00aa00" });
        var transaction = (await (await client.PostAsJsonAsync("/api/Transaction/Filter", new { }))
            .ReadAsync<PaginatedList<TransactionOutputDto>>()).Items.Single();
        var rule = (await (await client.GetAsync("/api/CategoryRules")).ReadAsync<List<CategoryRuleOutputDto>>())
            .First(r => r.Category.Id == DefaultCategories.Groceries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<CategoryOutputDto>();
        Assert.Equal(("Food", "#00AA00", 1), (updated.Name, updated.Color, updated.TransactionCount));
        Assert.Equal((DefaultCategories.Groceries, "Food", "#00AA00"), (transaction.Category.Id, transaction.Category.Name, transaction.Category.Color));
        Assert.Equal("Food", rule.Category.Name);
    }

    [Fact]
    public async Task Update_ToAnotherCategorysName_Is409_ButItsOwnNameInAnotherCaseIsFine()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var clash = await client.PutAsJsonAsync($"{Categories}/{DefaultCategories.Pet}", new { name = "Dining", color = "#123456" });
        var ownName = await client.PutAsJsonAsync($"{Categories}/{DefaultCategories.Pet}", new { name = "PET", color = "#123456" });
        var unknown = await client.PutAsJsonAsync($"{Categories}/{Guid.NewGuid()}", new { name = "X", color = "#123456" });

        Assert.Equal(
            [HttpStatusCode.Conflict, HttpStatusCode.OK, HttpStatusCode.NotFound],
            [clash.StatusCode, ownName.StatusCode, unknown.StatusCode]);
    }

    [Fact]
    public async Task TheBuiltInCategories_CanBeRenamed()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync($"{Categories}/{Category.OtherId}", new { name = "Uncategorised", color = "#999999" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await response.ReadAsync<CategoryOutputDto>()).BuiltInReason);
    }

    // ---- Using a new category

    [Fact]
    public async Task ANewCategory_CanBePickedForATransaction_UsedByARule_AndCountedByABudget()
    {
        await using var factory = new FinanceApiFactory(services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-09-15T12:00:00Z")))));
        var client = factory.CreateClient();
        var coffee = await AddAsync(client, "Coffee", "#6F4E37");
        var transaction = Stored("CAFE LATTE", DefaultCategories.Other);
        await factory.SeedAsync(db => db.Transactions.Add(transaction));

        var picked = await client.PutAsJsonAsync($"/api/Transaction/{transaction.Id}", new { description = "CAFE LATTE", categoryId = coffee.Id });
        await client.PostAsJsonAsync("/api/CategoryRules", new { keyword = "ESPRESSO", categoryId = coffee.Id, position = 0 });
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-09-04,ESPRESSO BAR,4.50,"));
        var budget = await (await client.PostAsJsonAsync("/api/Budget", new
        {
            name = "Coffee", description = "d", status = "Active", amount = 50, period = "Monthly", categoryIds = new[] { coffee.Id }
        })).ReadAsync<BudgetOutputDto>();
        var report = await (await client.GetAsync("/api/Reports/current-month-categorized-graph")).ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal("Coffee", (await picked.ReadAsync<TransactionOutputDto>()).Category.Name);
        Assert.Equal(coffee.Id, await factory.QueryAsync(db => db.Transactions.Where(t => t.Description == "ESPRESSO BAR").Select(t => t.CategoryId).SingleAsync()));
        Assert.Equal((14.50m, "Coffee"), (budget.TotalSpent, budget.Categories.Single().Name));
        Assert.Equal(14.50m, report.MonthlyData.Single().CategoryValues["Coffee"]);
        Assert.Contains(report.Categories, c => c.Name == "Coffee" && c.Color == "#6F4E37");
    }

    [Fact]
    public async Task AnUnknownCategory_IsRefusedWherever_ItIsPicked()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var transaction = Stored("LUNCH", DefaultCategories.Other);
        await factory.SeedAsync(db => db.Transactions.Add(transaction));
        var unknown = Guid.NewGuid();

        var update = await client.PutAsJsonAsync($"/api/Transaction/{transaction.Id}", new { description = "LUNCH", categoryId = unknown });
        var add = await client.PostAsJsonAsync("/api/Transaction", new
        {
            date = "2026-09-10", description = "X", amount = 5, transactionType = "Debit", accountId = Account.DefaultCreditCardId, categoryId = unknown
        });
        var rule = await client.PostAsJsonAsync("/api/CategoryRules", new { keyword = "X", categoryId = unknown });
        var preview = await client.PostAsJsonAsync("/api/CategoryRules/Preview", new { keyword = "X", categoryId = unknown });

        Assert.All([update, add, rule, preview], r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Contains("categoryId", await update.Content.ReadAsStringAsync());
    }

    // ---- Deleting

    [Fact]
    public async Task Delete_MovesItsTransactionsAndRulesToOther_AndItsBudgetStopsCountingIt()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var coffee = await AddAsync(client, "Coffee");
        var visible = Stored("CAFE", coffee.Id);
        var hidden = Stored("CAFE AGAIN", coffee.Id, hidden: true);
        await factory.SeedAsync(db => db.Transactions.AddRange(visible, hidden));
        await client.PostAsJsonAsync("/api/CategoryRules", new { keyword = "ESPRESSO", categoryId = coffee.Id });
        var budget = await (await client.PostAsJsonAsync("/api/Budget", new
        {
            name = "Treats", description = "d", status = "Active", amount = 50, period = "Monthly", categoryIds = new[] { coffee.Id }
        })).ReadAsync<BudgetOutputDto>();

        var response = await client.DeleteAsync($"{Categories}/{coffee.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await ListAsync(client), c => c.Id == coffee.Id);
        var moved = await factory.QueryAsync(db => db.Transactions.IgnoreQueryFilters().Select(t => t.CategoryId).Distinct().ToListAsync());
        Assert.Equal([Category.OtherId], moved);
        var rule = (await (await client.GetAsync("/api/CategoryRules")).ReadAsync<List<CategoryRuleOutputDto>>()).Single(r => r.Keyword == "ESPRESSO");
        Assert.Equal("Other", rule.Category.Name);
        Assert.Empty((await (await client.GetAsync($"/api/Budget/{budget.Id}")).ReadAsync<BudgetOutputDto>()).Categories);
    }

    [Fact]
    public async Task Delete_CanMoveEverythingToAChosenCategory()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var coffee = await AddAsync(client, "Coffee");
        await factory.SeedAsync(db => db.Transactions.Add(Stored("CAFE", coffee.Id)));

        var response = await client.DeleteAsync($"{Categories}/{coffee.Id}?moveTo={DefaultCategories.Dining}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(DefaultCategories.Dining, await factory.QueryAsync(db => db.Transactions.Select(t => t.CategoryId).SingleAsync()));
    }

    [Fact]
    public async Task Delete_IsRefused_ForTheBuiltInOnes_IntoItself_IntoNothing_AndForAnUnknownOne()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var coffee = await AddAsync(client, "Coffee");

        var other = await client.DeleteAsync($"{Categories}/{Category.OtherId}");
        var rent = await client.DeleteAsync($"{Categories}/{Category.RentId}");
        var intoItself = await client.DeleteAsync($"{Categories}/{coffee.Id}?moveTo={coffee.Id}");
        var intoNothing = await client.DeleteAsync($"{Categories}/{coffee.Id}?moveTo={Guid.NewGuid()}");
        var unknown = await client.DeleteAsync($"{Categories}/{Guid.NewGuid()}");

        Assert.Equal(
            [HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.NotFound],
            [other.StatusCode, rent.StatusCode, intoItself.StatusCode, intoNothing.StatusCode, unknown.StatusCode]);
        Assert.Contains("Built in", await rent.Content.ReadAsStringAsync());
        Assert.Equal(12, (await ListAsync(client)).Count);
    }

    // ---- Starter rules

    [Fact]
    public async Task StarterRules_FollowARenamedDefaultCategory_AndSkipADeletedOne()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await client.PutAsJsonAsync($"{Categories}/{DefaultCategories.Pet}", new { name = "Animals", color = "#8C564B" });
        // Utilities' rules move to Other when it is deleted; then its keywords are free for the pack to take again.
        await client.DeleteAsync($"{Categories}/{DefaultCategories.Utilities}");
        await factory.SeedAsync(db =>
        {
            db.CategoryRules.RemoveRange(db.CategoryRules.Where(r => r.Keyword == "TRUPANION" || r.Keyword == "ENMAX"));
            db.SeedMarkers.RemoveRange(db.SeedMarkers.Where(m => m.Name == SeedMarker.RulePack("Neo")));
        });

        await factory.Services.SeedRulesAsync(NullLogger.Instance);

        var rules = await (await client.GetAsync("/api/CategoryRules")).ReadAsync<List<CategoryRuleOutputDto>>();
        Assert.Equal("Animals", rules.Single(r => r.Keyword == "TRUPANION").Category.Name);
        Assert.DoesNotContain(rules, r => r.Keyword == "ENMAX");
    }
}

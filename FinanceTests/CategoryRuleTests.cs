using System.Net;
using System.Net.Http.Json;
using Finance.Application.Import;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceTests;

/// <summary>Category rules stored in the database: seeding, editing, and their use at import and when re-applied.</summary>
public class CategoryRuleTests
{
    private const string Rules = "/api/CategoryRules";

    // The initial rules, as shipped in import-rules.json.
    private static readonly List<CategoryRule> SeedRules = new ConfigurationBuilder()
        .AddJsonFile("import-rules.json").Build()
        .GetSection(ImportOptions.SectionName).Get<ImportOptions>()!.ToCategoryRules();

    private static Transaction Stored(string description, Category category, AccountType account = AccountType.CreditCard,
        bool locked = false, bool hidden = false) => new()
    {
        Id = Guid.NewGuid(), Date = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), Description = description, Amount = 10,
        Category = category, TransactionType = TransactionType.Debit, Account = account, Hash = Guid.NewGuid().ToString(),
        CategoryLocked = locked, Hidden = hidden
    };

    private static async Task<List<CategoryRuleOutputDto>> GetRulesAsync(HttpClient client) =>
        await (await client.GetAsync(Rules)).ReadAsync<List<CategoryRuleOutputDto>>();

    private static async Task<CategoryRuleOutputDto> AddRuleAsync(HttpClient client, string keyword, string category, int? position = null)
    {
        var response = await client.PostAsJsonAsync(Rules, new { keyword, category, position });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<CategoryRuleOutputDto>();
    }

    private static void AssertContiguous(List<CategoryRuleOutputDto> rules) =>
        Assert.Equal(Enumerable.Range(0, rules.Count), rules.Select(r => r.Position));

    // ---- Seeding

    [Fact]
    public async Task Startup_StoresTheRulesFromImportRulesJson_OneRulePerKeyword_InOrder()
    {
        await using var factory = new FinanceApiFactory();

        var rules = await GetRulesAsync(factory.CreateClient());

        Assert.True(SeedRules.Count > 50);
        Assert.Equal(SeedRules.Select(r => (r.Keyword, r.Category)), rules.Select(r => (r.Keyword, r.Category)));
        Assert.Equal(("METRO", Category.Groceries), (rules[0].Keyword, rules[0].Category));
        AssertContiguous(rules);
    }

    [Fact]
    public async Task Seeding_HappensOnce_AndNotAgainAfterEveryRuleWasDeleted()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        // A second start on the same database adds nothing.
        await factory.Services.SeedCategoryRulesAsync(NullLogger.Instance);
        Assert.Equal(SeedRules.Count, (await GetRulesAsync(client)).Count);

        foreach (var rule in await GetRulesAsync(client))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Rules}/{rule.Id}")).StatusCode);
        }
        await factory.Services.SeedCategoryRulesAsync(NullLogger.Instance);

        Assert.Empty(await GetRulesAsync(client));
    }

    // ---- Editing

    [Fact]
    public async Task Add_TrimsAndUpperCasesTheKeyword_AndAppendsByDefault()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var added = await AddRuleAsync(client, "  sq *Corner Store ", "Groceries");

        Assert.Equal(("SQ *CORNER STORE", Category.Groceries, SeedRules.Count), (added.Keyword, added.Category, added.Position));
        var rules = await GetRulesAsync(client);
        Assert.Equal(added.Id, rules[^1].Id);
        AssertContiguous(rules);
    }

    [Fact]
    public async Task Add_AtAPosition_InsertsItThere_AndMovesTheRestDown()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var first = await AddRuleAsync(client, "CORNER STORE", "Groceries", position: 0);
        var third = await AddRuleAsync(client, "BAKERY", "Dining", position: 2);
        var pastTheEnd = await AddRuleAsync(client, "FLORIST", "Shopping", position: 10_000);

        var rules = await GetRulesAsync(client);
        Assert.Equal(["CORNER STORE", "METRO", "BAKERY", "GROCERY"], rules.Take(4).Select(r => r.Keyword));
        Assert.Equal((0, 2, rules.Count - 1), (first.Position, third.Position, pastTheEnd.Position));
        AssertContiguous(rules);
    }

    [Theory]
    [InlineData("metro")]
    [InlineData(" Metro ")]
    public async Task Add_AKeywordThatExists_IgnoringCase_Returns409(string keyword)
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync(Rules, new { keyword, category = "Dining" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("METRO", (await response.ReadAsync<ProblemDetails>()).Detail);
    }

    public static TheoryData<string?, string?, int?> InvalidRules => new()
    {
        { "   ", "Dining", null },
        { null, "Dining", null },
        { new string('A', 101), "Dining", null },
        { "BAKERY", null, null },
        { "BAKERY", "Travel", null },
        { "BAKERY", "Dining", -1 },
    };

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public async Task Add_InvalidValues_Return400(string? keyword, string? category, int? position)
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Rules, new { keyword, category, position });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SeedRules.Count, (await GetRulesAsync(client)).Count);
    }

    [Fact]
    public async Task Update_ChangesKeywordAndCategory_AndKeepsThePosition()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var metro = (await GetRulesAsync(client))[0];

        var response = await client.PutAsJsonAsync($"{Rules}/{metro.Id}", new { keyword = " metro inc ", category = "Shopping" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync<CategoryRuleOutputDto>();
        Assert.Equal(("METRO INC", Category.Shopping, 0), (updated.Keyword, updated.Category, updated.Position));
        Assert.Equal("METRO INC", (await GetRulesAsync(client))[0].Keyword);
    }

    [Fact]
    public async Task Update_ToAnotherRulesKeyword_Returns409_ButItsOwnKeywordIsFine()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var metro = (await GetRulesAsync(client))[0];

        var taken = await client.PutAsJsonAsync($"{Rules}/{metro.Id}", new { keyword = "grocery", category = "Groceries" });
        var own = await client.PutAsJsonAsync($"{Rules}/{metro.Id}", new { keyword = "metro", category = "Dining" });

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task UnknownRule_Returns404()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"{Rules}/{id}", new { keyword = "X", category = "Dining" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{Rules}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"{Rules}/{id}/Move", new { position = 0 })).StatusCode);
    }

    [Fact]
    public async Task MoveAndDelete_KeepPositionsContiguous()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var rules = await GetRulesAsync(client);
        var (first, second, last) = (rules[0], rules[1], rules[^1]);

        var afterMoveUp = await (await client.PostAsJsonAsync($"{Rules}/{last.Id}/Move", new { position = 0 }))
            .ReadAsync<List<CategoryRuleOutputDto>>();
        var afterMoveDown = await (await client.PostAsJsonAsync($"{Rules}/{first.Id}/Move", new { position = 10_000 }))
            .ReadAsync<List<CategoryRuleOutputDto>>();
        await client.DeleteAsync($"{Rules}/{second.Id}");
        var afterDelete = await GetRulesAsync(client);

        Assert.Equal([last.Id, first.Id, second.Id], afterMoveUp.Take(3).Select(r => r.Id));
        Assert.Equal([last.Id, second.Id], afterMoveDown.Take(2).Select(r => r.Id));
        Assert.Equal(first.Id, afterMoveDown[^1].Id);
        Assert.Equal(rules.Count - 1, afterDelete.Count);
        Assert.DoesNotContain(afterDelete, r => r.Id == second.Id);
        AssertContiguous(afterMoveUp);
        AssertContiguous(afterMoveDown);
        AssertContiguous(afterDelete);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(null)]
    public async Task Move_WithoutAValidPosition_Returns400(int? position)
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var rule = (await GetRulesAsync(client))[0];

        var response = await client.PostAsJsonAsync($"{Rules}/{rule.Id}/Move", new { position });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Using the rules

    [Fact]
    public async Task Import_UsesARuleAddedInTheApp()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-09-03,SQ *CORNER STORE #12 TORONTO,12.50,"));

        await AddRuleAsync(client, "corner store", "Groceries");
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-09-04,SQ *CORNER STORE #12 TORONTO,8.00,"));

        var categories = await factory.QueryAsync(db => db.Transactions.OrderBy(t => t.Date).Select(t => t.Category).ToListAsync());
        Assert.Equal([Category.Other, Category.Groceries], categories);
    }

    [Fact]
    public async Task Import_TheFirstMatchingRuleByPositionWins()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        // "METRO" (Groceries) is the first seeded rule; this one goes above it.
        await AddRuleAsync(client, "METRO PASS", "Transportation", position: 0);

        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("""
            2026-09-03,METRO PASS TORONTO,156.00,
            2026-09-04,METRO #123 OSHAWA,40.00,
            """));

        var categories = await factory.QueryAsync(db => db.Transactions.OrderBy(t => t.Date).Select(t => t.Category).ToListAsync());
        Assert.Equal([Category.Transportation, Category.Groceries], categories);
    }

    [Fact]
    public async Task Import_RentPayments_AreCategorisedByTheRules_AndCountedOnce()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-01-01,VALIANT RENTAL PAYMENT,1900.00,", "Chequing"));
        var valiant = (await GetRulesAsync(client)).Single(r => r.Keyword == "VALIANT");
        await client.DeleteAsync($"{Rules}/{valiant.Id}");
        await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-02-01,VALIANT RENTAL PAYMENT,1900.00,", "Chequing"));

        // Nothing generated and nothing dropped: the payments as paid, in whatever category the rules said then.
        var stored = await factory.QueryAsync(db => db.Transactions.OrderBy(t => t.Date)
            .Select(t => new { t.Date.Month, t.Description, t.Amount, t.Category }).ToListAsync());
        Assert.Equal([
            new { Month = 1, Description = "VALIANT RENTAL PAYMENT", Amount = 1900.00m, Category = Category.Rent },
            new { Month = 2, Description = "VALIANT RENTAL PAYMENT", Amount = 1900.00m, Category = Category.Other },
        ], stored);
    }

    [Fact]
    public async Task ApplyCategoryRules_UsesTheStoredRules_AndStillSkipsHandPickedSavingsHiddenAndGeneratedRent()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var corner = Stored("SQ *CORNER STORE #12", Category.Other);
        var walmart = Stored("WAL-MART #3059", Category.Groceries);           // its seeded rule is deleted below
        var handPicked = Stored("CORNER STORE #9", Category.Pet, locked: true);
        var savings = Stored("CORNER STORE TRANSFER", Category.Other, AccountType.Savings);
        var hidden = Stored("CORNER STORE #3", Category.Other, hidden: true);
        // Rent generated before real payments were imported: the migration locked its category.
        var fixedRent = Stored("Monthly Rent", Category.Rent, AccountType.Chequing, locked: true);
        await factory.SeedAsync(db => db.Transactions.AddRange(corner, walmart, handPicked, savings, hidden, fixedRent));

        await AddRuleAsync(client, "CORNER STORE", "Groceries");
        await AddRuleAsync(client, "MONTHLY RENT", "Utilities", position: 0);
        await client.DeleteAsync($"{Rules}/{(await GetRulesAsync(client)).Single(r => r.Keyword == "WAL-MART").Id}");
        var result = await (await client.PostAsync("/api/Transaction/ApplyCategoryRules", null)).ReadAsync<CategoryRulesResultDto>();

        Assert.Equal((2, 2), (result.Checked, result.Updated));
        var categories = await factory.QueryAsync(db => db.Transactions.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id, t => t.Category));
        Assert.Equal(Category.Groceries, categories[corner.Id]);
        Assert.Equal(Category.Other, categories[walmart.Id]);
        Assert.Equal(Category.Pet, categories[handPicked.Id]);
        Assert.Equal(Category.Other, categories[savings.Id]);
        Assert.Equal(Category.Other, categories[hidden.Id]);
        Assert.Equal(Category.Rent, categories[fixedRent.Id]);
    }

    [Fact]
    public async Task Preview_CountsWhatApplyingTheRuleFirstWouldChange()
    {
        await using var factory = new FinanceApiFactory();
        var edited = Stored("SQ *CORNER STORE #12", Category.Other);
        var others = Enumerable.Range(1, 6).Select(n => Stored($"SQ *CORNER STORE #{n % 3}", Category.Other)).ToList();
        var seededRuleSaysShopping = Stored("CORNER STORE @ WALMART", Category.Shopping);
        var alreadyGroceries = Stored("CORNER STORE #7", Category.Groceries);
        var skipped = new[]
        {
            Stored("CORNER STORE #9", Category.Pet, locked: true),
            Stored("CORNER STORE TRANSFER", Category.Other, AccountType.Savings),
            Stored("CORNER STORE #3", Category.Other, hidden: true),
            Stored("SOMEWHERE ELSE", Category.Other),
        };
        await factory.SeedAsync(db =>
        {
            db.Transactions.AddRange([edited, .. others, seededRuleSaysShopping, alreadyGroceries, .. skipped]);
        });

        var response = await factory.CreateClient().PostAsJsonAsync($"{Rules}/Preview",
            new { keyword = " corner store ", category = "Groceries", excludeTransactionId = edited.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.ReadAsync<CategoryRulePreviewDto>();
        Assert.Equal(7, preview.Count);
        Assert.Equal(4, preview.Examples.Count);
        Assert.All(preview.Examples, e => Assert.Contains("CORNER STORE", e));
        Assert.Contains("CORNER STORE @ WALMART", preview.Examples);
        // Nothing is stored by a preview.
        Assert.Equal(SeedRules.Count, await factory.QueryAsync(db => db.CategoryRules.CountAsync()));
    }

    [Fact]
    public async Task Preview_ShowsAtMostFiveExamples()
    {
        await using var factory = new FinanceApiFactory();
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Enumerable.Range(1, 8).Select(n => Stored($"BAKERY #{n}", Category.Other))));

        var preview = await (await factory.CreateClient().PostAsJsonAsync($"{Rules}/Preview",
            new { keyword = "bakery", category = "Dining" })).ReadAsync<CategoryRulePreviewDto>();

        Assert.Equal((8, 5), (preview.Count, preview.Examples.Count));
    }

    [Fact]
    public async Task Preview_InvalidRule_Returns400()
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync($"{Rules}/Preview", new { keyword = "", category = "Dining" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

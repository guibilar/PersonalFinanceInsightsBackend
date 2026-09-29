using System.Net;
using System.Net.Http.Json;
using System.Text;
using Finance.Application.Import;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Finance.Infrastructure.Import;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceTests;

/// <summary>Neo Financial statements: the format per account, its parser, and its starter rules.</summary>
public class NeoImportTests
{
    // A card export: header line, signed amounts, padded descriptions, and one row still pending.
    private const string NeoCard = """
        Transaction Date,Posted Date,Status,Description,Amount
        2026-08-31,2026-09-01,Posted,FARMER'S FRIDGE CALGAR CALGARY       CAN,-10.70
        2026-08-28,2026-08-28,Posted,Payment Received - Thank you,2158.23
        2026-08-27,2026-08-29,Posted,AIR CAN*               WINNIPEG      CAN,-2125.52
        2026-08-19,2026-08-21,Posted,BEST BUY EXPRESS 118   CALGARY       CAN,-31.49
        2026-08-15,2026-08-16,Posted,CYNTHIA'S NO FRILLS 69 CALGARY       CAN,-26.49
        2026-08-15,2026-08-16,Posted,CYNTHIA'S NO FRILLS 69 CALGARY       CAN,-26.49
        2026-08-14,2026-08-15,Posted,SHOPPERS DRUG MART #23 CALGARY       CAN,25.00
        2026-09-01,,Pending,COFFEE PLACE            CALGARY       CAN,-4.50
        """;

    private static List<Transaction> Parse(string csv) =>
        new NeoCsvParser().Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv))).Transactions;

    private static async Task<Guid> AddAccountAsync(HttpClient client, string name, AccountType type, StatementFormat format)
    {
        var response = await client.PostAsJsonAsync("/api/Accounts", new { name, type, format }, FinanceApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<AccountOutputDto>()).Id;
    }

    // ---- Parser

    [Fact]
    public void Parser_ReadsSignedAmounts_CollapsesPaddingAndSkipsPendingRows()
    {
        var parsed = Parse(NeoCard);

        Assert.Equal(
        [
            ("FARMER'S FRIDGE CALGAR CALGARY CAN", 10.70m, TransactionType.Debit),
            ("Payment Received - Thank you", 2158.23m, TransactionType.Credit),
            ("AIR CAN* WINNIPEG CAN", 2125.52m, TransactionType.Debit),
            ("BEST BUY EXPRESS 118 CALGARY CAN", 31.49m, TransactionType.Debit),
            ("CYNTHIA'S NO FRILLS 69 CALGARY CAN", 26.49m, TransactionType.Debit),
            ("CYNTHIA'S NO FRILLS 69 CALGARY CAN", 26.49m, TransactionType.Debit),
            ("SHOPPERS DRUG MART #23 CALGARY CAN", 25.00m, TransactionType.Credit)
        ], parsed.Select(t => (t.Description, t.Amount, t.TransactionType!.Value)));
        // The transaction date, not the day it posted.
        Assert.Equal(new DateTime(2026, 8, 31), parsed[0].Date);
    }

    [Fact]
    public void Parser_FindsColumnsByName_InAnyOrder_AndWithoutAStatusColumn()
    {
        var parsed = Parse("""
            "Amount","Description","Transaction Date"
            "-1,234.50","RENT ","2026-08-01"
            """);

        var rent = Assert.Single(parsed);
        Assert.Equal(("RENT", 1234.50m, TransactionType.Debit), (rent.Description, rent.Amount, rent.TransactionType));
    }

    [Theory]
    [InlineData("2026-01-03,METRO,8.00,")]                        // a CIBC file: no header
    [InlineData("Date,Description,Amount\n2026-01-03,METRO,8.00")] // another layout
    [InlineData("")]
    public void Parser_FileWithoutNeoHeaders_IsRejected(string content)
    {
        var ex = Assert.Throws<InvalidStatementException>(() => Parse(content));

        Assert.Contains("Neo", ex.Message);
    }

    [Fact]
    public void Parser_HeaderWithoutRows_IsRejected_ButOnlyPendingRowsIsNot()
    {
        Assert.Throws<InvalidStatementException>(() => Parse("Transaction Date,Posted Date,Status,Description,Amount"));

        var onlyPending = Parse("""
            Transaction Date,Posted Date,Status,Description,Amount
            2026-09-01,,Pending,COFFEE PLACE,-4.50
            """);
        Assert.Empty(onlyPending);
    }

    [Fact]
    public void CibcParser_RejectsANeoFile_InsteadOfReadingNothing()
    {
        var ex = Assert.Throws<InvalidStatementException>(() =>
            new CibcCsvParser().Parse(new MemoryStream(Encoding.UTF8.GetBytes(NeoCard))));

        Assert.Contains("Neo", ex.Message);
    }

    // ---- Accounts

    [Fact]
    public async Task Accounts_HaveAFormat_CibcByDefault_AndItCanBeCorrected()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var defaults = await (await client.GetAsync("/api/Accounts")).ReadAsync<List<AccountOutputDto>>();
        var plain = await (await client.PostAsJsonAsync("/api/Accounts", new { name = "Visa", type = "CreditCard" })).ReadAsync<AccountOutputDto>();
        var neoId = await AddAccountAsync(client, "Neo card", AccountType.CreditCard, StatementFormat.Neo);
        var renamed = await (await client.PutAsJsonAsync($"/api/Accounts/{neoId}", new { name = "Neo World Elite" })).ReadAsync<AccountOutputDto>();
        var corrected = await (await client.PutAsJsonAsync($"/api/Accounts/{neoId}", new { name = "Neo World Elite", format = "Cibc" })).ReadAsync<AccountOutputDto>();
        var invalid = await client.PutAsJsonAsync($"/api/Accounts/{neoId}", new { name = "Neo World Elite", format = "Amex" });

        Assert.All(defaults, a => Assert.Equal(StatementFormat.Cibc, a.Format));
        Assert.Equal(StatementFormat.Cibc, plain.Format);
        Assert.Equal(StatementFormat.Neo, renamed.Format);   // left out: unchanged
        Assert.Equal(StatementFormat.Cibc, corrected.Format);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    // ---- Import

    [Fact]
    public async Task Import_IntoANeoAccount_ReadsTheFile_AppliesTheStarterRules_AndReimportAddsNothing()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var accountId = await AddAccountAsync(client, "Neo card", AccountType.CreditCard, StatementFormat.Neo);

        var first = await (await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(NeoCard, accountId.ToString())))
            .ReadAsync<ImportResultDto>();
        var again = await (await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(NeoCard, accountId.ToString())))
            .ReadAsync<ImportResultDto>();

        // Seven posted rows, both identical same-day purchases kept.
        Assert.Equal((7, 7, 0), (first.Read, first.Added, first.Skipped));
        Assert.Equal((7, 0, 7), (again.Read, again.Added, again.Skipped));
        var listed = await (await client.PostAsJsonAsync("/api/Transaction/Filter", new { accountId, pageSize = 50 }))
            .ReadAsync<PaginatedList<TransactionOutputDto>>();
        var byDescription = listed.Items.GroupBy(t => t.Description).ToDictionary(g => g.Key, g => g.First());
        Assert.Equal((Category.Groceries, Flow.Spending), (byDescription["FARMER'S FRIDGE CALGAR CALGARY CAN"].Category, byDescription["FARMER'S FRIDGE CALGAR CALGARY CAN"].Flow));
        Assert.Equal(Category.Transportation, byDescription["AIR CAN* WINNIPEG CAN"].Category);
        Assert.Equal(Category.Shopping, byDescription["BEST BUY EXPRESS 118 CALGARY CAN"].Category);
        Assert.Equal(Category.Groceries, byDescription["CYNTHIA'S NO FRILLS 69 CALGARY CAN"].Category);
        // The shipped rules come first: "SHOPPERS" is a Shopping rule there, and a pack never overrides them.
        Assert.Equal(Category.Shopping, byDescription["SHOPPERS DRUG MART #23 CALGARY CAN"].Category);
        // Paying the card off is a transfer, not a refund that would cut the month's spending.
        Assert.Equal(Flow.Transfer, byDescription["Payment Received - Thank you"].Flow);
        Assert.Equal(Flow.Refund, byDescription["SHOPPERS DRUG MART #23 CALGARY CAN"].Flow);
        Assert.All(listed.Items, t => Assert.Equal("Neo card", t.AccountName));
    }

    [Fact]
    public async Task Import_ACibcFileIntoANeoAccount_AndTheOtherWayRound_ExplainsTheMismatch()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var neoId = await AddAccountAsync(client, "Neo card", AccountType.CreditCard, StatementFormat.Neo);

        var cibcIntoNeo = await client.PostAsync("/api/Transaction/Import", ImportTests.Statement("2026-01-03,METRO,8.00,", neoId.ToString()));
        var neoIntoCibc = await client.PostAsync("/api/Transaction/Import", ImportTests.Statement(NeoCard, "CreditCard"));

        foreach (var response in new[] { cibcIntoNeo, neoIntoCibc })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.ReadAsync<HttpValidationProblemDetails>();
            Assert.Contains("Neo", Assert.Single(problem.Errors["file"]));
        }
        Assert.Equal(0, await factory.QueryAsync(db => db.Transactions.CountAsync()));
    }

    // ---- Rule pack

    [Fact]
    public async Task NeoRulePack_IsStoredAfterTheExistingRules_OncePerDatabase_AndLeavesEditedRulesAlone()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var rules = await (await client.GetAsync("/api/CategoryRules")).ReadAsync<List<CategoryRuleOutputDto>>();
        var flows = await (await client.GetAsync("/api/FlowRules")).ReadAsync<List<FlowRuleOutputDto>>();

        Assert.Equal(Category.Pet, rules.Single(r => r.Keyword == "TRUPANION").Category);
        Assert.Equal(Flow.Transfer, flows.Single(r => r.Keyword == "PAYMENT RECEIVED - THANK YOU").Flow);
        Assert.Equal(Enumerable.Range(0, rules.Count), rules.Select(r => r.Position));
        // After the shipped rules, so a keyword both would match is still decided by them.
        Assert.True(rules.Single(r => r.Keyword == "MINISO").Position > rules.Single(r => r.Keyword == "METRO").Position);

        // Deleted, edited and re-seeded: it stays deleted and edited (the pack was seeded already).
        var trupanion = rules.Single(r => r.Keyword == "TRUPANION");
        await client.DeleteAsync($"/api/CategoryRules/{trupanion.Id}");
        var telus = rules.Single(r => r.Keyword == "TELUS");
        await client.PutAsJsonAsync($"/api/CategoryRules/{telus.Id}", new { keyword = "TELUS", category = "Other" });
        await factory.Services.SeedRulesAsync(NullLogger.Instance);
        var after = await (await client.GetAsync("/api/CategoryRules")).ReadAsync<List<CategoryRuleOutputDto>>();
        Assert.DoesNotContain(after, r => r.Keyword == "TRUPANION");
        Assert.Equal(Category.Other, after.Single(r => r.Keyword == "TELUS").Category);
    }

    [Fact]
    public async Task NeoRulePack_ReachesADatabaseSeededBeforeIt_WithoutOverridingItsRules()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        // The state of a database from before the pack existed: some of its rules missing, no pack marker, and its own
        // rule for a keyword the pack also has.
        var before = await (await client.GetAsync("/api/CategoryRules")).ReadAsync<List<CategoryRuleOutputDto>>();
        var packKeywords = new[] { "TRUPANION", "MINISO", "ENMAX" };
        foreach (var rule in before.Where(r => packKeywords.Contains(r.Keyword))) await client.DeleteAsync($"/api/CategoryRules/{rule.Id}");
        await client.PostAsJsonAsync("/api/CategoryRules", new { keyword = "ENMAX", category = "Entertainment" });
        var flow = (await (await client.GetAsync("/api/FlowRules")).ReadAsync<List<FlowRuleOutputDto>>())
            .Single(r => r.Keyword == "PAYMENT RECEIVED - THANK YOU");
        await client.DeleteAsync($"/api/FlowRules/{flow.Id}");
        await factory.SeedAsync(db => db.SeedMarkers.RemoveRange(db.SeedMarkers.Where(m => m.Name == SeedMarker.RulePack("Neo"))));

        await factory.Services.SeedRulesAsync(NullLogger.Instance);
        await factory.Services.SeedRulesAsync(NullLogger.Instance);

        var rules = await (await client.GetAsync("/api/CategoryRules")).ReadAsync<List<CategoryRuleOutputDto>>();
        Assert.Equal(Category.Pet, rules.Single(r => r.Keyword == "TRUPANION").Category);
        Assert.Equal(Category.Entertainment, rules.Single(r => r.Keyword == "ENMAX").Category);   // the user's rule wins
        Assert.Equal(rules.Count, rules.Select(r => r.Keyword).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, rules.Count), rules.Select(r => r.Position));
        Assert.Contains(await (await client.GetAsync("/api/FlowRules")).ReadAsync<List<FlowRuleOutputDto>>(),
            r => r.Keyword == "PAYMENT RECEIVED - THANK YOU");
    }

    [Fact]
    public void RulePack_LeavesOutKeywordsThatHaveARule_AndSpendingFlowRules()
    {
        var pack = new RulePack
        {
            CategoryRules = [new CategoryKeywords { Category = Category.Shopping, Keywords = ["miniso", " Simons ", "MINISO", ""] }],
            FlowRules = [new FlowKeywords { Flow = Flow.Spending, Keywords = ["NEVER"] },
                         new FlowKeywords { Flow = Flow.Transfer, Keywords = ["CARD PAYMENT", "card payment"] }]
        };

        var categories = pack.ToCategoryRules(new HashSet<string> { "SIMONS" }, firstPosition: 7);
        var flows = pack.ToFlowRules(new HashSet<string>());

        Assert.Equal([("MINISO", 7)], categories.Select(r => (r.Keyword, r.Position)));
        Assert.Equal([("CARD PAYMENT", Flow.Transfer)], flows.Select(r => (r.Keyword, r.Flow)));
    }
}

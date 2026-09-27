using System.Net;
using System.Text;
using Finance.Application.Import;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Helpers;
using Finance.Infrastructure.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FinanceTests;

public class ImportTests
{
    // Rules as shipped in import-rules.json.
    private static readonly ImportOptions Rules = new ConfigurationBuilder()
        .AddJsonFile("import-rules.json").Build()
        .GetSection(ImportOptions.SectionName).Get<ImportOptions>()!;

    private static List<Transaction> Parse(string csv, AccountType account, ImportOptions? options = null) =>
        new CibcCsvParser(Options.Create(options ?? Rules)).Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)), account);

    // ---- Hash

    [Fact]
    public void Hash_FirstOccurrence_KeepsTheFormatOfAlreadyStoredHashes()
    {
        // sha256("45.102026-01-03metro #123 oshawa"): the format used before occurrences existed.
        Assert.Equal("29d9b96dd1f583f9ab10f9bab4d7dbad17b7f523164ee1439208fee58ca3bd26",
            TransactionHashHelper.GenerateTransactionHash(45.10m, new DateTime(2026, 1, 3), " METRO #123 OSHAWA "));
    }

    [Fact]
    public void Hash_LaterOccurrences_AreDistinct()
    {
        var date = new DateTime(2026, 1, 3);
        var hashes = Enumerable.Range(1, 3)
            .Select(n => TransactionHashHelper.GenerateTransactionHash(2.15m, date, "TIM HORTONS", n))
            .ToList();

        Assert.Equal(3, hashes.Distinct().Count());
    }

    // ---- Parser

    [Fact]
    public void Parser_CategorisesUsingRulesInOrder()
    {
        var parsed = Parse("""
            2026-01-03,WAL-MART #3059,20.00,
            2026-01-04,WALMART.CA,30.00,
            2026-01-05,SOMETHING NEW,5.00,
            """, AccountType.CreditCard);

        Assert.Equal([Category.Groceries, Category.Shopping, Category.Other], parsed.Select(t => t.Category));
    }

    [Fact]
    public void Parser_SkipsExcludedRowsPaymentsAndInvalidLines()
    {
        var parsed = Parse("""
            2026-01-03,Internet Banking INTERNET TRANSFER 01,300.00,
            2026-01-04,UBER* PENDING,12.00,
            2026-01-05,PAYMENT THANK YOU,,500.00
            not-a-date,METRO,1.00,
            2026-01-06,,1.00,
            2026-01-07,METRO,8.00,
            """, AccountType.CreditCard);

        var only = Assert.Single(parsed);
        Assert.Equal(("METRO", 8.00m, TransactionType.Debit), (only.Description, only.Amount, only.TransactionType));
    }

    [Fact]
    public void Parser_ChequingImportsKnownIncomeAndOneRentPerMonth()
    {
        var parsed = Parse("""
            2026-01-01,VALIANT RENTAL PAYMENT,1900.00,
            2026-01-15,Deel Canada PAYROLL,,4200.00
            2026-01-21,E-TRANSFER FROM FRIEND,,40.00
            2026-02-10,BELL CANADA,85.00,
            """, AccountType.Chequing);

        Assert.Equal(4200m, Assert.Single(parsed, t => t.TransactionType == TransactionType.Credit).Amount);
        var rent = parsed.Where(t => t.Category == Category.Rent).ToList();
        Assert.Equal([new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)], rent.Select(t => t.Date));
        Assert.All(rent, t => Assert.Equal(("Monthly Rent", 1947.50m), (t.Description, t.Amount)));
    }

    [Fact]
    public void Parser_WithoutMonthlyRent_KeepsRealRentPayments()
    {
        var options = new ImportOptions { CategoryRules = Rules.CategoryRules };

        var parsed = Parse("2026-01-01,VALIANT RENTAL PAYMENT,1900.00,", AccountType.Chequing, options);

        Assert.Equal(1900m, Assert.Single(parsed).Amount);
    }

    [Fact]
    public void Parser_SavingsReadsBothDirections()
    {
        var parsed = Parse("""
            2026-01-15,TRANSFER IN,,1000.00
            2026-02-15,TRANSFER OUT,250.00,
            """, AccountType.Savings);

        Assert.Equal([(TransactionType.Credit, 1000m), (TransactionType.Debit, 250m)],
            parsed.Select(t => (t.TransactionType!.Value, t.Amount)));
    }

    // ---- Endpoint

    internal static MultipartFormDataContent Statement(string csv, string? account = "CreditCard")
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "statement.csv" } };
        if (account != null) form.Add(new StringContent(account), "account");
        return form;
    }

    [Fact]
    public async Task Import_KeepsIdenticalSameDayPurchases_AndReimportAddsNothing()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        const string csv = """
            2026-01-03,TIM HORTONS #55,2.15,
            2026-01-03,TIM HORTONS #55,2.15,
            2026-01-04,METRO,45.10,
            """;

        var first = await (await client.PostAsync("/api/Transaction/Import", Statement(csv))).ReadAsync<ImportResultDto>();
        var again = await (await client.PostAsync("/api/Transaction/Import", Statement(csv))).ReadAsync<ImportResultDto>();

        Assert.Equal((3, 3, 0), (first.Read, first.Added, first.Skipped));
        Assert.Equal((3, 0, 3), (again.Read, again.Added, again.Skipped));
        Assert.Equal(2, await factory.QueryAsync(db => db.Transactions.CountAsync(t => t.Description == "TIM HORTONS #55")));
    }

    [Fact]
    public async Task Import_OverlappingStatement_AddsOnlyNewRows()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        await client.PostAsync("/api/Transaction/Import", Statement("""
            2026-01-03,TIM HORTONS #55,2.15,
            2026-01-20,METRO,45.10,
            """));

        var result = await (await client.PostAsync("/api/Transaction/Import", Statement("""
            2026-01-20,METRO,45.10,
            2026-02-02,NETFLIX.COM,16.99,
            """))).ReadAsync<ImportResultDto>();

        Assert.Equal((2, 1, 1), (result.Read, result.Added, result.Skipped));
    }

    [Fact]
    public async Task Import_MatchesTransactionsStoredWithTheOldHash()
    {
        await using var factory = new FinanceApiFactory();
        await factory.SeedAsync(db => db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(), Date = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Description = " METRO #123 OSHAWA ",
            Amount = 45.10m, Account = AccountType.CreditCard, TransactionType = TransactionType.Debit,
            Hash = "29d9b96dd1f583f9ab10f9bab4d7dbad17b7f523164ee1439208fee58ca3bd26"
        }));

        var result = await (await factory.CreateClient().PostAsync("/api/Transaction/Import",
            Statement("2026-01-03, METRO #123 OSHAWA ,45.10,"))).ReadAsync<ImportResultDto>();

        Assert.Equal(0, result.Added);
    }

    [Theory]
    [InlineData("2026-01-03,METRO,1.00,", null)]
    [InlineData("", "CreditCard")]
    [InlineData("2026-01-03,METRO,1.00,", "Bitcoin")]
    public async Task Import_InvalidRequest_Returns400(string csv, string? account)
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PostAsync("/api/Transaction/Import", Statement(csv, account));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

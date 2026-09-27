using System.Net;
using System.Text;
using Finance.Application.Import;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Finance.Domain.Helpers;
using Finance.Infrastructure.Import;
using Microsoft.AspNetCore.Http;
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
    public void Parser_CreditCardRefundsAreCredits_InThePurchaseCategory_ButCardPaymentsAreSkipped()
    {
        var parsed = Parse("""
            2026-01-03,WAL-MART #3059,120.00,
            2026-01-10,WAL-MART #3059,,120.00
            2026-01-12,PAYMENT THANK YOU/PAIEMENT MERCI,,500.00
            2026-01-13,payment thank you,,50.00
            2026-01-14,Internet Banking INTERNET TRANSFER 01,,20.00
            """, AccountType.CreditCard);

        Assert.Equal(
            [(TransactionType.Debit, 120m, Category.Groceries), (TransactionType.Credit, 120m, Category.Groceries)],
            parsed.Select(t => (t.TransactionType!.Value, t.Amount, t.Category)));
    }

    [Fact]
    public void Parser_ChequingMoneyInIsOnlyKnownIncome()
    {
        var parsed = Parse("""
            2026-01-15,E-TRANSFER FROM A FRIEND,,40.00
            2026-01-16,METRO,,5.00
            """, AccountType.Chequing, new ImportOptions
            {
                IncomeKeywords = Rules.IncomeKeywords, ExcludedDescriptions = Rules.ExcludedDescriptions,
                CategoryRules = Rules.CategoryRules, MonthlyRent = null
            });

        Assert.Empty(parsed);
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
    public void Parser_MonthlyRent_UsesTheAmountThatAppliedThatMonth()
    {
        var options = new ImportOptions
        {
            CategoryRules = Rules.CategoryRules,
            MonthlyRent = new MonthlyRentOptions
            {
                Amount = 1900, Description = "Monthly Rent",
                // In any order.
                Changes = [new RentChange { From = "2026-04", Amount = 2100 }, new RentChange { From = "2026-02", Amount = 2000 }]
            }
        };

        var parsed = Parse("""
            2026-01-10,BELL CANADA,85.00,
            2026-02-10,BELL CANADA,85.00,
            2026-03-10,BELL CANADA,85.00,
            2026-04-10,BELL CANADA,85.00,
            """, AccountType.Chequing, options);

        Assert.Equal([1900m, 2000m, 2000m, 2100m], parsed.Where(t => t.Category == Category.Rent).Select(t => t.Amount));
    }

    [Theory]
    [InlineData("2026/02", 2000)]
    [InlineData("February 2026", 2000)]
    [InlineData("2026-02", 0)]
    public void RentChange_NeedsAMonthAsYearDashMonth_AndAPositiveAmount(string from, decimal amount)
    {
        var rent = new MonthlyRentOptions { Amount = 1900, Changes = [new RentChange { From = from, Amount = amount }] };

        Assert.False(rent.IsValid());
    }

    [Fact]
    public async Task InvalidRentChange_StopsTheApiAtStartup()
    {
        await using var factory = new FinanceApiFactory(settings: new Dictionary<string, string?>
        {
            ["Import:MonthlyRent:Changes:0:From"] = "2027/01",
            ["Import:MonthlyRent:Changes:0:Amount"] = "2050",
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Import:MonthlyRent:Changes", ex.ToString());
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

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-8-bom")]
    [InlineData("windows-1252")]
    public void Parser_ReadsUtf8AndWindows1252(string encoding)
    {
        const string csv = "2026-01-03,CAFÉ DÉPANNEUR – MONTRÉAL,4.50,";
        var bytes = encoding switch
        {
            "utf-8" => Encoding.UTF8.GetBytes(csv),
            "utf-8-bom" => [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(csv)],
            _ => CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes(csv)
        };

        var parsed = new CibcCsvParser(Options.Create(Rules)).Parse(new MemoryStream(bytes), AccountType.CreditCard);

        Assert.Equal("CAFÉ DÉPANNEUR – MONTRÉAL", Assert.Single(parsed).Description);
    }

    [Fact]
    public void Parser_ShortRows_AreSkipped_NotAnError()
    {
        // A trimmed money-in column, and a row with only a date.
        var parsed = Parse("""
            2026-01-03,METRO,8.00
            2026-01-04
            2026-01-05,NETFLIX.COM,16.99,
            """, AccountType.CreditCard);

        Assert.Equal(["METRO", "NETFLIX.COM"], parsed.Select(t => t.Description));
    }

    public static TheoryData<string> FilesWithoutTransactionRows => new()
    {
        "Date,Description,Amount",                        // a header and nothing else
        "Transaction Date;Amount\n03/01/2026 x;12,50",    // another bank's layout
        "{\"not\": \"a csv\"}",
        "\"unterminated quote,1.00,\n",
    };

    [Theory]
    [MemberData(nameof(FilesWithoutTransactionRows))]
    public void Parser_FileWithoutAnyTransactionRow_IsRejected(string content)
    {
        var ex = Assert.Throws<InvalidStatementException>(() => Parse(content, AccountType.CreditCard));

        Assert.Contains("CIBC", ex.Message);
    }

    [Fact]
    public void Parser_StatementWhoseRowsAreAllSkipped_IsNotAnError()
    {
        // Still a statement: this month only has a card payment.
        Assert.Empty(Parse("2026-01-05,PAYMENT THANK YOU,,500.00", AccountType.CreditCard));
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
    public async Task Import_SameDayRefundOfAPurchase_IsKept_AndReimportAddsNothing()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        const string csv = """
            2026-01-03,WAL-MART #3059,45.10,
            2026-01-03,WAL-MART #3059,,45.10
            """;

        var first = await (await client.PostAsync("/api/Transaction/Import", Statement(csv))).ReadAsync<ImportResultDto>();
        var again = await (await client.PostAsync("/api/Transaction/Import", Statement(csv))).ReadAsync<ImportResultDto>();

        Assert.Equal((2, 2), (first.Read, first.Added));
        Assert.Equal((0, 2), (again.Added, again.Skipped));
        Assert.Equal([TransactionType.Credit, TransactionType.Debit], await factory.QueryAsync(db =>
            db.Transactions.Select(t => t.TransactionType!.Value).OrderBy(t => t).ToListAsync()));
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

    private static Transaction StoredRent(DateTime month, decimal amount, bool hidden = false) => new()
    {
        Id = Guid.NewGuid(), Date = DateTime.SpecifyKind(month, DateTimeKind.Utc), Description = "Monthly Rent", Amount = amount,
        Category = Category.Rent, TransactionType = TransactionType.Debit, Account = AccountType.Chequing,
        Hash = TransactionHashHelper.GenerateTransactionHash(amount, month, "Monthly Rent"), Hidden = hidden
    };

    [Fact]
    public async Task Import_AMonthThatAlreadyHasRent_GetsNoSecondOne_AndItsAmountFollowsTheRules()
    {
        // The rules: 1947.50, and 2050 from February. January and February were imported before that change.
        await using var factory = new FinanceApiFactory(settings: new Dictionary<string, string?>
        {
            ["Import:MonthlyRent:Changes:0:From"] = "2026-02",
            ["Import:MonthlyRent:Changes:0:Amount"] = "2050",
        });
        await factory.SeedAsync(db =>
        {
            db.Transactions.Add(StoredRent(new DateTime(2026, 1, 1), 1947.50m));
            db.Transactions.Add(StoredRent(new DateTime(2026, 2, 1), 1947.50m));
        });

        var result = await (await factory.CreateClient().PostAsync("/api/Transaction/Import", Statement("""
            2026-01-10,BELL CANADA,85.00,
            2026-02-10,BELL CANADA,85.00,
            2026-03-10,BELL CANADA,85.00,
            """, "Chequing"))).ReadAsync<ImportResultDto>();

        // Read: three bills and three rents; January's and February's rents are already there.
        Assert.Equal((6, 4, 2), (result.Read, result.Added, result.Skipped));
        var rents = await factory.QueryAsync(db => db.Transactions.Where(t => t.Category == Category.Rent)
            .OrderBy(t => t.Date).Select(t => t.Amount).ToListAsync());
        Assert.Equal([1947.50m, 2050m, 2050m], rents);
    }

    [Fact]
    public async Task Import_DeletedRent_StaysDeleted()
    {
        await using var factory = new FinanceApiFactory();
        await factory.SeedAsync(db => db.Transactions.Add(StoredRent(new DateTime(2026, 1, 1), 1800, hidden: true)));

        var result = await (await factory.CreateClient().PostAsync("/api/Transaction/Import",
            Statement("2026-01-10,BELL CANADA,85.00,", "Chequing"))).ReadAsync<ImportResultDto>();

        Assert.Equal((2, 1, 1), (result.Read, result.Added, result.Skipped));
        Assert.Equal(0, await factory.QueryAsync(db => db.Transactions.CountAsync(t => t.Category == Category.Rent)));
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

    public static TheoryData<byte[]> UnreadableFiles => new()
    {
        Encoding.UTF8.GetBytes("Posted Date,Payee,Amount\n"),
        new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0xFF, 0xFE, 0x0A, 0x01 }, // a zip (e.g. an .xlsx) renamed to .csv
    };

    [Theory]
    [MemberData(nameof(UnreadableFiles))]
    public async Task Import_UnreadableFile_Returns400WithAMessageForTheFileField(byte[] content)
    {
        await using var factory = new FinanceApiFactory();
        var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", "statement.csv" } };
        form.Add(new StringContent("CreditCard"), "account");

        var response = await factory.CreateClient().PostAsync("/api/Transaction/Import", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadAsync<HttpValidationProblemDetails>();
        Assert.Contains("CIBC", Assert.Single(problem.Errors["file"]));
        Assert.Equal(0, await factory.QueryAsync(db => db.Transactions.CountAsync()));
    }

    [Fact]
    public async Task Import_RowsWithFewerColumns_AreImported()
    {
        await using var factory = new FinanceApiFactory();

        // Three columns: used to fail with a missing-field error (500).
        var response = await factory.CreateClient().PostAsync("/api/Transaction/Import",
            Statement("2026-01-03,COFFEE,4.50\n2026-01-04,TEA,3.00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.ReadAsync<ImportResultDto>()).Added);
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

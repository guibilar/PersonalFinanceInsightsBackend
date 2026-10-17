using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Finance.Application.Events;
using Finance.Application.Import;
using Finance.Application.Reports;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Finance.Domain.Helpers;
using Finance.Infrastructure.Import;
using Hestia.Contracts;
using Hestia.Contracts.Finance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

public class ImportTests
{
    // Rules as shipped in import-rules.json.
    private static readonly ImportOptions Rules = new ConfigurationBuilder()
        .AddJsonFile("import-rules.json").Build()
        .GetSection(ImportOptions.SectionName).Get<ImportOptions>()!;

    private static readonly TransactionClassifier Classifier =
        new(new CategoryMatcher(Rules.ToCategoryRules(DefaultCategories.IdOf)), new FlowMatcher(Rules.ToFlowRules()));

    private static ParsedStatement ParseStatement(string csv) =>
        new CibcCsvParser().Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)));

    /// <summary>Parsed, then categorised and given a flow by the shipped rules, as an import into that kind of account.</summary>
    private static List<Transaction> Parse(string csv, AccountType account)
    {
        var transactions = ParseStatement(csv).Transactions;
        transactions.ForEach(t => Classifier.Classify(t, account));
        return transactions;
    }

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

        Assert.Equal([DefaultCategories.Groceries, DefaultCategories.Shopping, DefaultCategories.Other], parsed.Select(t => t.CategoryId));
    }

    [Fact]
    public void Parser_KeepsEveryMovement_TransfersAndPendingHoldsAreNotSpending()
    {
        var parsed = Parse("""
            2026-01-03,Internet Banking INTERNET TRANSFER 01,300.00,
            2026-01-04,UBER* PENDING,12.00,
            2026-01-05,PAYMENT THANK YOU,,500.00
            not-a-date,METRO,1.00,
            2026-01-06,,1.00,
            2026-01-07,METRO,8.00,
            """, AccountType.CreditCard);

        Assert.Equal(
        [
            ("Internet Banking INTERNET TRANSFER 01", TransactionType.Debit, Flow.Transfer),
            ("UBER* PENDING", TransactionType.Debit, Flow.Ignored),
            ("PAYMENT THANK YOU", TransactionType.Credit, Flow.Transfer),
            ("METRO", TransactionType.Debit, Flow.Spending)
        ], parsed.Select(t => (t.Description, t.TransactionType!.Value, t.Flow)));
    }

    [Fact]
    public void Parser_CreditCardRefundsAreRefunds_InThePurchaseCategory_AndCardPaymentsAreTransfers()
    {
        var parsed = Parse("""
            2026-01-03,WAL-MART #3059,120.00,
            2026-01-10,WAL-MART #3059,,120.00
            2026-01-12,PAYMENT THANK YOU/PAIEMENT MERCI,,500.00
            2026-01-13,payment thank you,,50.00
            2026-01-14,Internet Banking INTERNET TRANSFER 01,,20.00
            """, AccountType.CreditCard);

        Assert.Equal(
        [
            (TransactionType.Debit, 120m, DefaultCategories.Groceries, Flow.Spending),
            (TransactionType.Credit, 120m, DefaultCategories.Groceries, Flow.Refund),
            (TransactionType.Credit, 500m, DefaultCategories.Other, Flow.Transfer),
            (TransactionType.Credit, 50m, DefaultCategories.Other, Flow.Transfer),
            (TransactionType.Credit, 20m, DefaultCategories.Other, Flow.Transfer)
        ], parsed.Select(t => (t.TransactionType!.Value, t.Amount, t.CategoryId, t.Flow)));
    }

    [Fact]
    public void Parser_ChequingDeposits_AreIncome_UnlessARuleSaysOtherwise()
    {
        // Used to be dropped unless they matched an income keyword.
        var parsed = Parse("""
            2026-01-15,E-TRANSFER FROM A FRIEND,,40.00
            2026-01-16,Internet Banking INTERNET TRANSFER 000123,,500.00
            2026-01-17,Deel Canada PAYROLL,,4200.00
            """, AccountType.Chequing);

        Assert.Equal([Flow.Income, Flow.Transfer, Flow.Income], parsed.Select(t => t.Flow));
    }

    [Fact]
    public void FlowRules_LongestKeywordWins_AndIncomeRulesOnlyApplyToMoneyIn()
    {
        var flows = new FlowMatcher([
            new FlowRule { Keyword = "TRANSFER", Flow = Flow.Transfer },
            new FlowRule { Keyword = "e-transfer from", Flow = Flow.Income },
            new FlowRule { Keyword = "AMAZON", Flow = Flow.Refund }
        ]);

        Assert.Equal(Flow.Income, flows.Match("E-TRANSFER FROM BOSS", AccountType.Chequing, TransactionType.Credit));
        Assert.Equal(Flow.Transfer, flows.Match("INTERNET TRANSFER", AccountType.Chequing, TransactionType.Credit));
        // Money out can't be income or a refund: the next rule, or the default, decides.
        Assert.Equal(Flow.Transfer, flows.Match("E-TRANSFER FROM BOSS", AccountType.Chequing, TransactionType.Debit));
        Assert.Equal(Flow.Spending, flows.Match("AMAZON.CA", AccountType.CreditCard, TransactionType.Debit));
        Assert.Equal(Flow.Refund, flows.Match("AMAZON.CA", AccountType.Chequing, TransactionType.Credit));
    }

    [Theory]
    [InlineData(AccountType.Chequing, TransactionType.Debit, Flow.Spending)]
    [InlineData(AccountType.Chequing, TransactionType.Credit, Flow.Income)]
    [InlineData(AccountType.CreditCard, TransactionType.Debit, Flow.Spending)]
    [InlineData(AccountType.CreditCard, TransactionType.Credit, Flow.Refund)]
    [InlineData(AccountType.Savings, TransactionType.Debit, Flow.Transfer)]
    [InlineData(AccountType.Savings, TransactionType.Credit, Flow.Transfer)]
    public void FlowRules_WithoutAMatch_TheAccountAndDirectionDecide(AccountType account, TransactionType direction, Flow expected)
    {
        Assert.Equal(expected, new FlowMatcher([]).Match("ANYTHING", account, direction));
    }

    [Fact]
    public void Parser_RecognisesCreditCardExports_ByTheCardNumberColumn()
    {
        Assert.True(ParseStatement("2026-01-03,METRO,8.00,,4500********1234").IsCreditCardExport);
        Assert.False(ParseStatement("2026-01-03,METRO,8.00,").IsCreditCardExport);
        Assert.False(ParseStatement("2026-01-03,METRO,8.00,,a note").IsCreditCardExport);
    }

    [Fact]
    public void Parser_ChequingImportsIncomeAndTheRealRentPayment()
    {
        var parsed = Parse("""
            2026-01-01,VALIANT RENTAL PAYMENT,1947.50,
            2026-01-15,Deel Canada PAYROLL,,4200.00
            2026-01-21,E-TRANSFER FROM FRIEND,,40.00
            2026-02-10,BELL CANADA,85.00,
            """, AccountType.Chequing);

        Assert.Equal(4200m, Assert.Single(parsed, t => t.Description.StartsWith("Deel")).Amount);
        // The payment as the bank shows it: no generated rent, and none for February, which has no payment.
        var rent = Assert.Single(parsed, t => t.CategoryId == DefaultCategories.Rent);
        Assert.Equal(("VALIANT RENTAL PAYMENT", 1947.50m, new DateTime(2026, 1, 1)), (rent.Description, rent.Amount, rent.Date));
    }

    [Fact]
    public void Parser_CreditCardRowsInTheRentCategory_AreKept()
    {
        // Used to be dropped without a trace: rent-category rows were replaced by a generated rent on chequing only.
        var parsed = Parse("""
            2026-01-10,VALIANT STORAGE UNIT,120.00,
            2026-01-11,METRO,10.00,
            """, AccountType.CreditCard);

        Assert.Equal([("VALIANT STORAGE UNIT", DefaultCategories.Rent), ("METRO", DefaultCategories.Groceries)],
            parsed.Select(t => (t.Description, t.CategoryId)));
    }

    [Fact]
    public void Parser_SavingsReadsBothDirections()
    {
        var parsed = Parse("""
            2026-01-15,TRANSFER IN,,1000.00
            2026-02-15,TRANSFER OUT,250.00,
            """, AccountType.Savings);

        // Transfers from and to the other accounts; not purchases, so no category.
        Assert.Equal([(TransactionType.Credit, 1000m, Flow.Transfer), (TransactionType.Debit, 250m, Flow.Transfer)],
            parsed.Select(t => (t.TransactionType!.Value, t.Amount, t.Flow)));
        Assert.All(parsed, t => Assert.Equal(DefaultCategories.Other, t.CategoryId));
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

        var parsed = new CibcCsvParser().Parse(new MemoryStream(bytes));

        Assert.Equal("CAFÉ DÉPANNEUR – MONTRÉAL", Assert.Single(parsed.Transactions).Description);
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
    public void Rules_MatchADescriptionThePaddedBankWayToo()
    {
        var matcher = new CategoryMatcher([new CategoryRule { Keyword = "TIM HORTONS", CategoryId = DefaultCategories.IdOf("Dining")!.Value, Position = 0 }]);

        Assert.Equal(DefaultCategories.IdOf("Dining"), matcher.Categorize("TIM  HORTONS #55"));
        Assert.Equal("TIM HORTONS", MerchantNames.Normalize("TIM \tHORTONS #55"));
    }

    [Fact]
    public void Parser_RefusesDatesASpreadsheetRewrote_RatherThanSwappingDaysAndMonths()
    {
        var ex = Assert.Throws<InvalidStatementException>(() => Parse("""
            05/01/2026,METRO,8.00,
            13/01/2026,METRO,9.00,
            """, AccountType.CreditCard));

        Assert.Contains("05/01/2026", ex.Message);
    }

    [Fact]
    public void Parser_StatementWithOnlyACardPayment_IsNotAnError()
    {
        // Still a statement: this month only has a card payment, which isn't spending.
        Assert.Equal(Flow.Transfer, Assert.Single(Parse("2026-01-05,PAYMENT THANK YOU,,500.00", AccountType.CreditCard)).Flow);
    }

    // ---- Endpoint

    /// <param name="account">
    /// The kind of one of the default accounts ("CreditCard"), an account id, or anything else to send as the account id as is.
    /// </param>
    internal static MultipartFormDataContent Statement(string csv, string? account = "CreditCard", bool confirmed = false)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "statement.csv" } };
        if (account != null)
        {
            var accountId = Enum.TryParse<AccountType>(account, out var type) ? TestAccounts.Id(type).ToString() : account;
            form.Add(new StringContent(accountId), "accountId");
        }
        if (confirmed) form.Add(new StringContent("true"), "confirmed");
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

    /// <summary>A rent row generated before real payments were imported (the migration locked its category).</summary>
    private static Transaction GeneratedRent(DateTime month, bool hidden = false) => new()
    {
        Id = Guid.NewGuid(), Date = DateTime.SpecifyKind(month, DateTimeKind.Utc), Description = "Monthly Rent", Amount = 1947.50m,
        CategoryId = DefaultCategories.Rent, CategoryLocked = true, TransactionType = TransactionType.Debit, AccountId = Account.DefaultChequingId,
        Hash = TransactionHashHelper.GenerateTransactionHash(1947.50m, month, "Monthly Rent"), Hidden = hidden
    };

    [Fact]
    public async Task Import_RealRentPayment_ReplacesThatMonthsGeneratedRent_AndOnlyThatMonths()
    {
        await using var factory = new FinanceApiFactory();
        var january = GeneratedRent(new DateTime(2026, 1, 1));
        var february = GeneratedRent(new DateTime(2026, 2, 1));
        await factory.SeedAsync(db => db.Transactions.AddRange(january, february));

        // January's statement, re-imported now that rent comes in as paid (on the 3rd).
        var result = await (await factory.CreateClient().PostAsync("/api/Transaction/Import", Statement("""
            2026-01-03,VALIANT RENTAL PAYMENT,1947.50,
            2026-01-10,BELL CANADA,85.00,
            """, "Chequing"))).ReadAsync<ImportResultDto>();

        Assert.Equal((2, 2, 0), (result.Read, result.Added, result.Skipped));
        // Rent counts once per month: January the real payment, February (not re-imported) the generated row.
        var visibleRent = await factory.QueryAsync(db => db.Transactions.Where(t => t.CategoryId == DefaultCategories.Rent)
            .OrderBy(t => t.Date).Select(t => new { t.Date.Month, t.Description }).ToListAsync());
        Assert.Equal([new { Month = 1, Description = "VALIANT RENTAL PAYMENT" }, new { Month = 2, Description = "Monthly Rent" }],
            visibleRent);
        // Hidden, not deleted: it can still be restored.
        Assert.True(await factory.QueryAsync(db => db.Transactions.IgnoreQueryFilters()
            .Where(t => t.Id == january.Id).Select(t => t.Hidden).SingleAsync()));
    }

    /// <summary>The finance events on, without a broker: what the services raise, kept here.</summary>
    private sealed class RaisedEvents : IFinanceEvents
    {
        public ConcurrentQueue<IIntegrationEvent> All { get; } = new();
        public bool Enabled => true;
        public void Raise(IIntegrationEvent @event) => All.Enqueue(@event);
        public void Clear() { }
    }

    [Fact]
    public async Task Import_RealRentPayment_SendsTheGeneratedRentAsDeleted()
    {
        var events = new RaisedEvents();
        await using var factory = new FinanceApiFactory(services => services.Replace(ServiceDescriptor.Singleton<IFinanceEvents>(events)));
        var january = GeneratedRent(new DateTime(2026, 1, 1));
        await factory.SeedAsync(db => db.Transactions.Add(january));

        await factory.CreateClient().PostAsync("/api/Transaction/Import", Statement("2026-01-03,VALIANT RENTAL PAYMENT,1947.50,", "Chequing"));

        var deleted = Assert.Single(events.All.OfType<TransactionChanged>(), e => e.TransactionId == january.Id);
        Assert.Equal((TransactionChange.Deleted, "Monthly Rent", -1947.50m), (deleted.Change, deleted.Description, deleted.Amount));
    }

    [Fact]
    public async Task Import_WithoutARentPayment_LeavesTheGeneratedRentAlone()
    {
        await using var factory = new FinanceApiFactory();
        var january = GeneratedRent(new DateTime(2026, 1, 1));
        await factory.SeedAsync(db => db.Transactions.Add(january));

        await factory.CreateClient().PostAsync("/api/Transaction/Import", Statement("2026-01-10,BELL CANADA,85.00,", "Chequing"));
        // A rent-category purchase on the card (a storage unit) is not the apartment's rent.
        await factory.CreateClient().PostAsync("/api/Transaction/Import", Statement("2026-01-12,VALIANT STORAGE,50.00,", "CreditCard"));

        Assert.False(await factory.QueryAsync(db => db.Transactions.IgnoreQueryFilters()
            .Where(t => t.Id == january.Id).Select(t => t.Hidden).SingleAsync()));
    }

    [Fact]
    public async Task Import_MatchesTransactionsStoredWithTheOldHash()
    {
        await using var factory = new FinanceApiFactory();
        await factory.SeedAsync(db => db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(), Date = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Description = " METRO #123 OSHAWA ",
            Amount = 45.10m, AccountId = Account.DefaultCreditCardId, TransactionType = TransactionType.Debit,
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
        form.Add(new StringContent(Account.DefaultCreditCardId.ToString()), "accountId");

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

    [Fact]
    public async Task Import_UnknownAccount_Returns404()
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().PostAsync("/api/Transaction/Import",
            Statement("2026-01-03,METRO,1.00,", Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Import_StoresRowsOnThatAccount_WithTheirFlow()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var visa = await (await client.PostAsJsonAsync("/api/Accounts", new { name = "Visa", type = "CreditCard" }, FinanceApiFactory.Json))
            .ReadAsync<AccountOutputDto>();

        await client.PostAsync("/api/Transaction/Import", Statement("""
            2026-01-03,WAL-MART #3059,120.00,
            2026-01-10,PAYMENT THANK YOU,,500.00
            """, visa.Id.ToString()));
        var listed = await (await client.PostAsJsonAsync("/api/Transaction/Filter", new { accountId = visa.Id, sortBy = "Amount" }))
            .ReadAsync<PaginatedList<TransactionOutputDto>>();

        Assert.Equal(
            [("Visa", AccountType.CreditCard, 500m, Flow.Transfer), ("Visa", AccountType.CreditCard, 120m, Flow.Spending)],
            listed.Items.Select(t => (t.AccountName, t.AccountType, t.Amount, t.Flow)));
    }

    [Fact]
    public async Task Import_CreditCardExportIntoABankAccount_AsksForConfirmation()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        const string csv = "2026-01-03,METRO,8.00,,4500********1234";

        var refused = await client.PostAsync("/api/Transaction/Import", Statement(csv, "Chequing"));
        var confirmed = await client.PostAsync("/api/Transaction/Import", Statement(csv, "Chequing", confirmed: true));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await refused.ReadAsync<ProblemDetails>();
        Assert.Equal("account-mismatch", problem.Extensions["code"]?.ToString());
        Assert.Contains("credit card export", problem.Detail);
        Assert.Equal(1, (await confirmed.ReadAsync<ImportResultDto>()).Added);
    }

    [Fact]
    public async Task Import_StatementAlreadyStoredOnAnotherAccount_AsksForConfirmation()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        const string statement = """
            2026-01-03,METRO,8.00,
            2026-01-04,BELL CANADA,85.00,
            2026-01-05,NETFLIX.COM,16.99,
            2026-01-06,SHELL,40.00,
            """;
        await client.PostAsync("/api/Transaction/Import", Statement(statement, "Chequing"));

        // The same file again, with a new row, but into the card.
        var refused = await client.PostAsync("/api/Transaction/Import", Statement(statement + "\n2026-01-07,TIM HORTONS,2.00,"));
        // Re-importing it into the right account is fine, and so is a statement sharing only a row or two.
        var sameAccount = await client.PostAsync("/api/Transaction/Import", Statement(statement, "Chequing"));
        var fewShared = await client.PostAsync("/api/Transaction/Import", Statement("""
            2026-01-03,METRO,8.00,
            2026-01-08,LOBLAWS,60.00,
            2026-01-09,AMAZON.CA,20.00,
            """));

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("4 of the 5 transactions in this file are already in \"Chequing\"", (await refused.ReadAsync<ProblemDetails>()).Detail);
        Assert.Equal(HttpStatusCode.OK, sameAccount.StatusCode);
        Assert.Equal(3, (await fewShared.ReadAsync<ImportResultDto>()).Added);
    }
}

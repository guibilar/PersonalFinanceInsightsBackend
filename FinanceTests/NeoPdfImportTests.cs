using System.Net;
using System.Net.Http.Json;
using Finance.Application.Import;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Finance.Infrastructure.Import;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static FinanceTests.NeoStatementPdf;

namespace FinanceTests;

/// <summary>Neo's monthly PDF statements, read into a Neo account as its CSV exports are.</summary>
public class NeoPdfImportTests
{
    private static readonly byte[] February = Build("Feb 1, 2026 - Feb 28, 2026", 2404.22m, 10408.64m,
    [
        new("Feb 1", "Interest", 4.42m, 2408.64m),
        new("Feb 2", "Transfer from Everyday Spending", 4000m, 6408.64m),
        new("Feb 2", "Transfer from Everyday Spending", 1500m, 7908.64m),
        new("Feb 17", "Transfer from Everyday Spending", 3000m, 10908.64m),
        new("Feb 17", "Transfer to Everyday Spending", -500m, 10408.64m),
    ]);

    private static List<Transaction> Parse(byte[] pdf) => new NeoCsvParser().Parse(new MemoryStream(pdf)).Transactions;

    private static MultipartFormDataContent Upload(byte[] pdf, Guid accountId) => new()
    {
        { new ByteArrayContent(pdf), "file", "statement-february-28-2026.pdf" },
        { new StringContent(accountId.ToString()), "accountId" },
    };

    [Fact]
    public void Parser_ReadsTheTable_WithTheYearFromThePeriod_AndCreditsAndDebits()
    {
        var parsed = Parse(February);

        Assert.Equal(
        [
            (new DateTime(2026, 2, 1), "Interest", 4.42m, TransactionType.Credit),
            (new DateTime(2026, 2, 2), "Transfer from Everyday Spending", 4000m, TransactionType.Credit),
            (new DateTime(2026, 2, 2), "Transfer from Everyday Spending", 1500m, TransactionType.Credit),
            (new DateTime(2026, 2, 17), "Transfer from Everyday Spending", 3000m, TransactionType.Credit),
            (new DateTime(2026, 2, 17), "Transfer to Everyday Spending", 500m, TransactionType.Debit),
        ], parsed.Select(t => (t.Date, t.Description, t.Amount, t.TransactionType!.Value)));
        Assert.All(parsed, t => Assert.Equal(DateTimeKind.Utc, t.Date.Kind));
    }

    [Fact]
    public void Parser_GivesDecemberRowsTheYearBefore_InAStatementRunningIntoJanuary()
    {
        var parsed = Parse(Build("Dec 15, 2025 - Jan 14, 2026", 100m, 70m,
        [
            new("Dec 20", "Groceries", -40m, 60m),
            new("Jan 3", "Refund", 10m, 70m),
        ]));

        Assert.Equal([new DateTime(2025, 12, 20), new DateTime(2026, 1, 3)], parsed.Select(t => t.Date));
    }

    [Fact]
    public void Parser_GivesTheClosingBalance_OnThePeriodsLastDay()
    {
        var balance = new NeoCsvParser().Parse(new MemoryStream(February)).ClosingBalance;

        Assert.Equal(new StatementBalance(new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc), 10408.64m), balance);
    }

    [Fact]
    public void Parser_GivesARowMadeJustBeforeTheMonthTheYearBefore()
    {
        // Rows carry the day the purchase was made: Dec 31, posted Jan 2, is in January's statement.
        var parsed = Parse(Build("Jan 1, 2026 - Jan 31, 2026", 100m, 85.50m,
        [
            new("Dec 31", "Coffee", -4.50m, 95.50m),
            new("Jan 20", "Groceries", -10m, 85.50m),
        ]));

        Assert.Equal([new DateTime(2025, 12, 31), new DateTime(2026, 1, 20)], parsed.Select(t => t.Date));
    }

    [Fact]
    public void Parser_JoinsWrappedDescriptions_AndFollowsTheTableOntoMorePages()
    {
        var parsed = Parse(Build("Jul 1, 2026 - Jul 31, 2026", 545.47m, 1328.10m,
        [
            new("Jul 1", "Interest", 0.04m, 545.51m),
            new("Jul 14", "Interac e-transfer sent to\nSam Example for dinner", -12.50m, 533.01m),
        ],
        [
            new("Jul 30", "Transfer from Employer Inc", 2303.81m, 2836.82m),
            new("Jul 31", "Payment to Credit", -1508.72m, 1328.10m),
        ]));

        Assert.Equal(
            ["Interest", "Interac e-transfer sent to Sam Example for dinner", "Transfer from Employer Inc", "Payment to Credit"],
            parsed.Select(t => t.Description));
    }

    [Fact]
    public void Parser_RefusesAStatementWhoseRowsDontAddUp_RatherThanGuess()
    {
        var wrongRow = Build("Feb 1, 2026 - Feb 28, 2026", 100m, 90m,
        [
            new("Feb 3", "Coffee", -5m, 95m),
            new("Feb 4", "Lunch", -12m, 90m),
        ]);
        var wrongClosing = Build("Feb 1, 2026 - Feb 28, 2026", 100m, 80m, [new("Feb 3", "Coffee", -5m, 95m)]);

        var row = Assert.Throws<InvalidStatementException>(() => Parse(wrongRow));
        var closing = Assert.Throws<InvalidStatementException>(() => Parse(wrongClosing));

        Assert.Contains("Feb 4 Feb 4 Lunch -12.00 90.00", row.Message);
        Assert.Contains("from 95.00 to 90.00", row.Message);
        Assert.Contains("closing balance is 80.00", closing.Message);
    }

    [Fact]
    public void Parser_RefusesOtherPdfs_AndUnreadableOnes()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.Letter).AddText("An invoice", 12, new UglyToad.PdfPig.Core.PdfPoint(20, 700), font);

        var other = Assert.Throws<InvalidStatementException>(() => Parse(builder.Build()));
        var broken = Assert.Throws<InvalidStatementException>(() => Parse("%PDF-1.7 not really"u8.ToArray()));

        Assert.Contains("doesn't look like a Neo Financial statement", other.Message);
        Assert.Contains("can't be read", broken.Message);
    }

    [Fact]
    public async Task Import_APdfIntoANeoAccount_AddsItsRows_AndAgainAddsNothing()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/Accounts",
            new { name = "Neo savings", type = AccountType.Savings, format = StatementFormat.Neo }, FinanceApiFactory.Json);
        var accountId = (await created.ReadAsync<AccountOutputDto>()).Id;

        var first = await (await client.PostAsync("/api/Transaction/Import", Upload(February, accountId))).ReadAsync<ImportResultDto>();
        var again = await (await client.PostAsync("/api/Transaction/Import", Upload(February, accountId))).ReadAsync<ImportResultDto>();

        // The two same-day transfers of different amounts are both kept; a second import of the month skips all five.
        Assert.Equal((5, 5, 0), (first.Read, first.Added, first.Skipped));
        Assert.Equal((5, 0, 5), (again.Read, again.Added, again.Skipped));
    }

    [Fact]
    public async Task Import_PayingTheNeoCardFromEveryday_IsATransfer_AndAStatementThatDoesntAddUpAddsNothing()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/Accounts",
            new { name = "Neo everyday", type = AccountType.Chequing, format = StatementFormat.Neo }, FinanceApiFactory.Json);
        var accountId = (await created.ReadAsync<AccountOutputDto>()).Id;
        var july = Build("Jul 1, 2026 - Jul 31, 2026", 500m, 300m, [new("Jul 14", "Payment to Credit", -200m, 300m)]);
        var broken = Build("Jul 1, 2026 - Jul 31, 2026", 500m, 300m, [new("Jul 14", "Payment to Credit", -250m, 300m)]);

        var refused = await client.PostAsync("/api/Transaction/Import", Upload(broken, accountId));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Nothing was imported", Assert.Single((await refused.ReadAsync<HttpValidationProblemDetails>()).Errors["file"]));
        Assert.Equal(0, await factory.QueryAsync(db => db.Transactions.CountAsync()));

        await client.PostAsync("/api/Transaction/Import", Upload(july, accountId));
        var payment = await factory.QueryAsync(db => db.Transactions.SingleAsync());
        Assert.Equal((Flow.Transfer, TransactionType.Debit), (payment.Flow, payment.TransactionType));
    }
}

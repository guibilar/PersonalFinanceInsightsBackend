using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Finance.Infrastructure.Import;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using static FinanceTests.WealthsimpleStatementPdf;

namespace FinanceTests;

/// <summary>Wealthsimple's monthly PDF statements: the cash activity, and the portfolio's value as the month's balance.</summary>
public class WealthsimplePdfImportTests
{
    private static readonly byte[] March = Build("2026-03-01 - 2026-03-31", 8_250.40m, 12.30m, 46.10m,
    [
        new("2026-03-02", "CONT", "Contribution (executed at 2026-03-02)", 0, 3_000m, 3_012.30m),
        new("2026-03-03", "BUY", "VFV - Vanguard S&P 500 Index ETF: Bought 20.0000\nshares (executed at 2026-03-03)", 2_980m, 0, 32.30m),
        new("2026-03-16", "STKDIV", "XYZ - Example Corp: stock dividend of 1.0000 share", 0, 0, 32.30m),
        new("2026-03-27", "DIV", "VFV - Vanguard S&P 500 Index ETF: Cash dividend", 0, 14.30m, 46.60m),
        new("2026-03-27", "WHTFED", "Federal withholding tax", 0.50m, 0, 46.10m),
    ]);

    private static List<Transaction> Parse(byte[] pdf) => new WealthsimplePdfParser().Parse(new MemoryStream(pdf)).Transactions;

    private static MultipartFormDataContent Upload(byte[] pdf, Guid accountId) => new()
    {
        { new ByteArrayContent(pdf), "file", "March_2026.pdf" },
        { new StringContent(accountId.ToString()), "accountId" },
    };

    [Fact]
    public void Parser_ReadsTheCashActivity_WithItsCodes_AndLeavesOutRowsThatMoveNoCash()
    {
        var parsed = Parse(March);

        Assert.Equal(
        [
            (new DateTime(2026, 3, 2), "CONT Contribution (executed at 2026-03-02)", 3_000m, TransactionType.Credit),
            (new DateTime(2026, 3, 3), "BUY VFV - Vanguard S&P 500 Index ETF: Bought 20.0000 shares (executed at 2026-03-03)", 2_980m, TransactionType.Debit),
            (new DateTime(2026, 3, 27), "DIV VFV - Vanguard S&P 500 Index ETF: Cash dividend", 14.30m, TransactionType.Credit),
            (new DateTime(2026, 3, 27), "WHTFED Federal withholding tax", 0.50m, TransactionType.Debit),
        ], parsed.Select(t => (t.Date, t.Description, t.Amount, t.TransactionType!.Value)));
        Assert.All(parsed, t => Assert.Equal(DateTimeKind.Utc, t.Date.Kind));
    }

    [Fact]
    public void Parser_KeepsTradesDividendsAndTaxesOutOfTransfers_AndLeavesDepositsToTheRules()
    {
        var parsed = Parse(March);

        Assert.Equal(
            [(false, Flow.Spending), (true, Flow.Ignored), (true, Flow.Ignored), (true, Flow.Spending)],
            parsed.Select(t => (t.FlowLocked, t.Flow)));
    }

    [Fact]
    public void Parser_GivesAFeeOrTaxBackAsARefund_AndABonusTakenBackAsNoIncome()
    {
        var parsed = Parse(Build("2026-04-01 - 2026-04-30", 100m, 10m, 10m,
        [
            new("2026-04-02", "WHTFED", "Withholding tax reversal", 0, 5m, 15m),
            new("2026-04-03", "REFER", "Referral bonus clawback", 5m, 0, 10m),
        ]));

        Assert.Equal(
            [(TransactionType.Credit, Flow.Refund), (TransactionType.Debit, Flow.Ignored)],
            parsed.Select(t => (t.TransactionType!.Value, t.Flow)));
    }

    [Fact]
    public void Parser_GivesThePortfolioValue_AtThePeriodsEnd_EvenWithNoActivity()
    {
        var statement = new WealthsimplePdfParser().Parse(new MemoryStream(Build("2025-05-01 - 2025-05-31", 3_845.03m, 0, 0, [])));

        Assert.Empty(statement.Transactions);
        Assert.Equal(new DateTime(2025, 5, 31), statement.ClosingBalance!.Date);
        Assert.Equal(3_845.03m, statement.ClosingBalance.Amount);
    }

    [Fact]
    public void Parser_FollowsTheActivityOntoMorePages()
    {
        var parsed = Parse(Build("2026-05-01 - 2026-05-31", 1_000m, 2_800.02m, 0,
            [new("2026-05-01", "TRFOUTTF", "Tax-free money transfer out of the account (executed at 2026-05-01)", 1_800.02m, 0, 1_000m)],
            [new("2026-05-20", "WD", "Withdrawal", 1_000m, 0, 0)]));

        Assert.Equal(["TRFOUTTF", "WD"], parsed.Select(t => t.Description.Split(' ')[0]));
    }

    [Fact]
    public void Parser_ReadsInteracETransfers_AsDeposits_AndKeepsWrappedLinesWithPrices()
    {
        var parsed = Parse(Build("2026-06-01 - 2026-06-30", 4_380.73m, 0, 0.02m,
        [
            new("2026-06-13", "E_TRFIN", "Interac e-Transfer Received", 0, 500m, 500m),
            new("2026-06-16", "BUY", "VFV - Vanguard S&P 500 Index ETF: Bought\n1.0626 shares at $188.20 per share (executed at 2026-06-15)", 499.98m, 0, 0.02m),
        ]));

        Assert.Equal(
        [
            ("E_TRFIN Interac e-Transfer Received", false),
            ("BUY VFV - Vanguard S&P 500 Index ETF: Bought 1.0626 shares at $188.20 per share (executed at 2026-06-15)", true),
        ], parsed.Select(t => (t.Description, t.FlowLocked)));
    }

    [Fact]
    public void Parser_MarksTransfersStraightFromOrToAnotherPlan_AndOnlyThose()
    {
        var parsed = Parse(Build("2026-05-01 - 2026-05-31", 1_000m, 0, 0,
        [
            new("2026-05-01", "TRFINTF", "Tax-free money transfer into the account", 0, 800m, 800m),
            new("2026-05-02", "CONT", "Contribution", 0, 200m, 1_000m),
            new("2026-05-20", "TRFOUTTF", "Tax-free money transfer out of the account", 1_000m, 0, 0),
        ]));

        Assert.Equal([true, false, true], parsed.Select(t => t.IsPlanTransfer));
    }

    [Fact]
    public void Parser_RefusesAStatementWhoseRowsDontAddUp_AndOtherPdfs()
    {
        var wrongRow = Build("2026-03-01 - 2026-03-31", 100m, 0, 50m,
            [new("2026-03-02", "CONT", "Contribution", 0, 100m, 100m), new("2026-03-03", "BUY", "Bought", 40m, 0, 50m)]);
        var wrongClosing = Build("2026-03-01 - 2026-03-31", 100m, 0, 80m, [new("2026-03-02", "CONT", "Contribution", 0, 100m, 100m)]);
        var neo = NeoStatementPdf.Build("Feb 1, 2026 - Feb 28, 2026", 0, 5m, [new("Feb 1", "Interest", 5m, 5m)]);

        var row = Assert.Throws<InvalidStatementException>(() => Parse(wrongRow));
        var closing = Assert.Throws<InvalidStatementException>(() => Parse(wrongClosing));
        var other = Assert.Throws<InvalidStatementException>(() => Parse(neo));
        var csv = Assert.Throws<InvalidStatementException>(() => Parse("date,amount\n"u8.ToArray()));

        Assert.Contains("2026-03-03 BUY Bought $40.00 $0.00 $50.00", row.Message);
        Assert.Contains("from 100.00 to 50.00", row.Message);
        Assert.Contains("closing cash balance is 80.00", closing.Message);
        Assert.Contains("doesn't look like a Wealthsimple statement", other.Message);
        Assert.Contains("doesn't look like a Wealthsimple statement", csv.Message);
    }

    [Fact]
    public async Task Import_IntoATfsa_CountsContributionsAgainstTheRoom_NotTfsaToTfsaTransfers_AndRecordsTheBalance()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/Accounts",
            new { name = "Wealthsimple TFSA", type = AccountType.Investment, format = StatementFormat.Wealthsimple, plan = RegisteredPlan.Tfsa, contributionRoom = 7_000m },
            FinanceApiFactory.Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var accountId = (await created.ReadAsync<AccountOutputDto>()).Id;
        var april = Build("2026-04-01 - 2026-04-30", 9_100m, 46.10m, 546.10m,
            [new("2026-04-10", "TRFINTF", "Tax-free money transfer into the account", 0, 500m, 546.10m)]);

        var first = await (await client.PostAsync("/api/Transaction/Import", Upload(March, accountId))).ReadAsync<ImportResultDto>();
        var again = await (await client.PostAsync("/api/Transaction/Import", Upload(March, accountId))).ReadAsync<ImportResultDto>();
        await client.PostAsync("/api/Transaction/Import", Upload(april, accountId));

        Assert.Equal((4, 4, 0), (first.Read, first.Added, first.Skipped));
        Assert.Equal((4, 0, 4), (again.Read, again.Added, again.Skipped));
        Assert.Equal((8_250.40m, new DateTime(2026, 3, 31)), (first.Balance, first.BalanceDate));
        var account = (await (await client.GetAsync("/api/Accounts")).ReadAsync<List<AccountOutputDto>>()).Single(a => a.Id == accountId);
        Assert.Equal((3_000m, 4_000m), (account.DepositedThisYear, account.RoomLeft));
        var balances = await factory.QueryAsync(db => db.AccountBalances.OrderBy(b => b.Date).Select(b => new { b.Date, b.Amount }).ToListAsync());
        Assert.Equal([(new DateTime(2026, 3, 31), 8_250.40m), (new DateTime(2026, 4, 30), 9_100m)], balances.Select(b => (b.Date, b.Amount)));
    }

    [Fact]
    public async Task Import_KeepsTheStatementsFlows_WhenTheRulesAreApplied()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/Accounts",
            new { name = "TFSA", type = AccountType.Investment, format = StatementFormat.Wealthsimple }, FinanceApiFactory.Json);
        var accountId = (await created.ReadAsync<AccountOutputDto>()).Id;
        await client.PostAsync("/api/Transaction/Import", Upload(March, accountId));

        await client.PostAsync("/api/Transaction/ApplyRules", null);

        var flows = await factory.QueryAsync(db => db.Transactions.OrderBy(t => t.Date).ThenBy(t => t.Amount).Select(t => t.Flow).ToListAsync());
        Assert.Equal([Flow.Transfer, Flow.Ignored, Flow.Spending, Flow.Ignored], flows);
    }
}

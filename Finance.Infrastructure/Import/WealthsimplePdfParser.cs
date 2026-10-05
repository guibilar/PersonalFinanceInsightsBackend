using System.Globalization;
using System.Text.RegularExpressions;
using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Finance.Infrastructure.Import;

/// <summary>
/// Wealthsimple's monthly PDF statements: the period at the top ("2026-05-01 - 2026-05-31"), the portfolio's value
/// ("Total Portfolio $3,845.03 …", market value first), the cash summary (last statement's and closing cash balance),
/// and the "Activity" table: the date, a statement code (CONT, BUY, DIV, TRFOUTTF, …), the description, the debit,
/// the credit and the cash balance after each row. Every row must take the cash balance to its own, and the last one to
/// the closing cash balance, or the statement is refused rather than imported with a row misread.
/// </summary>
/// <remarks>
/// Only the cash moves: buying a fund turns cash into the fund, so it isn't money in or out of the account. Rows are
/// imported with their code first ("BUY VFV - Vanguard …") and with a flow the code decides, which rules can't change:
/// only real deposits and withdrawals are transfers (the ones counted against a registered plan's room).
/// </remarks>
public partial class WealthsimplePdfParser : IStatementParser
{
    private const string NotWealthsimpleMessage =
        "This file doesn't look like a Wealthsimple statement. Expected the monthly PDF statement, with its Activity table and Portfolio Cash summary.";

    /// <summary>Codes that move money between the account and the person's other accounts: left to the flow rules (transfers by default).</summary>
    private static readonly HashSet<string> MoneyInOrOut =
    [
        "CONT", "DEP", "TRFIN", "TRFINTF", "WIREIN", "WIREINTF", "E_TRFIN", // in (E_: Interac e-Transfer)
        "WD", "WDQ", "TRFOUT", "TRFOUTTF", "HBP", "LLP", "OBP", "E_TRFOUT" // out
    ];

    /// <summary>Money the account earned or got as a bonus.</summary>
    private static readonly HashSet<string> Earned = ["AFFILIATE", "CLB", "CREDIT", "GIFTCARD", "GRANT", "REFER"];

    /// <summary>Costs: fees and taxes.</summary>
    private static readonly HashSet<string> Costs =
        ["DCTFEE", "DSCFEE", "FCPT", "FEE", "GST", "NRT", "PST", "QST", "TAX", "WHTFED", "WHTPROQC"];

    /// <summary>Words this close vertically are on the same line.</summary>
    private const double SameLine = 2;

    /// <summary>A wrapped description continues within this distance below its row.</summary>
    private const double WrappedLine = 16;

    private sealed record Line(double Y, List<Word> Words)
    {
        public string Text => string.Join(' ', Words.Select(w => w.Text));
        public double Left => Words[0].BoundingBox.Left;
    }

    private sealed record Row(DateTime Date, string Code, List<string> Description, decimal Debit, decimal Credit, decimal Balance, string Text);

    public StatementFormat Format => StatementFormat.Wealthsimple;

    public ParsedStatement Parse(Stream statement)
    {
        using var buffer = new MemoryStream();
        statement.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (!NeoPdfParser.IsPdf(bytes)) throw new InvalidStatementException(NotWealthsimpleMessage);

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(bytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidStatementException("This PDF can't be read. Is it a statement downloaded from Wealthsimple, not a scan or a protected file?");
        }

        using (document)
        {
            var pages = document.GetPages().Select(LinesOf).ToList();
            var all = pages.SelectMany(p => p).ToList();
            var end = Period(all);
            var opening = Summary(all, "Last Statement Cash Balance");
            var closing = Summary(all, "Closing Cash Balance");
            var portfolio = Summary(all, "Total Portfolio");

            var rows = new List<Row>();
            var inTable = false;
            foreach (var lines in pages)
            {
                Row? last = null;
                double lastY = 0, descriptionLeft = 0, amountsLeft = double.MaxValue;
                foreach (var line in lines)
                {
                    if (IsHeader(line))
                    {
                        // The table goes on over the next pages, under the same header (or none).
                        inTable = true;
                        descriptionLeft = line.Words.First(w => w.Text == "Description").BoundingBox.Left;
                        amountsLeft = line.Words.First(w => w.Text == "Debit").BoundingBox.Left;
                        last = null;
                        continue;
                    }
                    if (!inTable) continue;
                    if (TryRow(line, out var row))
                    {
                        rows.Add(row);
                        (last, lastY) = (row, line.Y);
                    }
                    else if (last is not null && lastY - line.Y <= WrappedLine && line.Left >= descriptionLeft - 2
                             && !line.Words.Any(w => w.BoundingBox.Left >= amountsLeft - 10))
                    {
                        // A description too long for its column goes on below it ("1.0626 shares at $188.20 per share").
                        last.Description.Add(line.Text);
                        lastY = line.Y;
                    }
                    else
                    {
                        last = null;
                    }
                }
            }
            if (!inTable) throw new InvalidStatementException(NotWealthsimpleMessage);

            Reconcile(rows, opening, closing);
            var transactions = rows
                // A stock dividend or a share lent out moves no cash.
                .Where(row => row.Credit != row.Debit)
                .Select(ToTransaction)
                .ToList();
            return new ParsedStatement(transactions, IsCreditCardExport: false, new StatementBalance(end, portfolio));
        }
    }

    private static Transaction ToTransaction(Row row)
    {
        var amount = row.Credit - row.Debit;
        var transaction = new Transaction
        {
            Date = row.Date,
            Description = $"{row.Code} {string.Join(' ', row.Description)}".Trim(),
            Amount = Math.Abs(amount),
            TransactionType = amount < 0 ? TransactionType.Debit : TransactionType.Credit,
        };
        if (!MoneyInOrOut.Contains(row.Code))
        {
            // Inside the account: buying and selling, dividends and interest, fees. Never a transfer, whatever the rules say.
            transaction.Flow = Costs.Contains(row.Code) ? Flow.Spending : Earned.Contains(row.Code) ? Flow.Income : Flow.Ignored;
            transaction.FlowLocked = true;
        }
        return transaction;
    }

    /// <summary>Each row moves the cash by its credit less its debit, from the last statement's cash to the closing cash.</summary>
    private static void Reconcile(List<Row> rows, decimal opening, decimal closing)
    {
        var balance = opening;
        foreach (var row in rows)
        {
            if (balance + row.Credit - row.Debit != row.Balance)
            {
                throw new InvalidStatementException(
                    $"Couldn't read this statement reliably: \"{row.Text}\" doesn't take the cash balance from {balance:N2} to {row.Balance:N2}. Nothing was imported.");
            }
            balance = row.Balance;
        }
        if (balance != closing)
        {
            throw new InvalidStatementException(
                $"Couldn't read this statement reliably: its activity ends at {balance:N2} in cash, but the closing cash balance is {closing:N2}. Nothing was imported.");
        }
    }

    /// <summary>"2026-05-01 TRFOUTTF Tax-free money transfer out … $2,800.02 $0.00 $0.00": date, code, description, debit, credit, balance.</summary>
    private static bool TryRow(Line line, out Row row)
    {
        row = null!;
        var words = line.Words.Select(w => w.Text).ToList();
        if (words.Count < 5 || !Code().IsMatch(words[1])
                            || !TryDate(words[0], out var date)
                            || !Money().IsMatch(words[^1]) || !Money().IsMatch(words[^2]) || !Money().IsMatch(words[^3]))
        {
            return false;
        }
        row = new Row(date, words[1], [string.Join(' ', words[2..^3])], Amount(words[^3]), Amount(words[^2]), Amount(words[^1]), line.Text);
        return true;
    }

    private static bool IsHeader(Line line)
    {
        var words = line.Words.Select(w => w.Text).ToList();
        return words.Contains("Transaction") && words.Contains("Description") && words.Contains("Debit")
               && words.Contains("Credit") && words.Contains("Balance");
    }

    /// <summary>The period's last day: "Statement Period" over "2026-05-01 - 2026-05-31", at the top of the first page.</summary>
    private static DateTime Period(List<Line> lines)
    {
        foreach (var line in lines)
        {
            var match = PeriodPattern().Match(line.Text);
            if (match.Success && TryDate(match.Groups[1].Value, out _) && TryDate(match.Groups[2].Value, out var end)) return end;
        }
        throw new InvalidStatementException(NotWealthsimpleMessage);
    }

    private static bool TryDate(string text, out DateTime date) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date);

    /// <summary>The first amount after the label: other columns can share its line ("… Balance $0.00 Cash Paid In").</summary>
    private static decimal Summary(List<Line> lines, string label)
    {
        foreach (var line in lines)
        {
            var at = line.Text.IndexOf(label + " ", StringComparison.Ordinal);
            if (at < 0) continue;
            var amount = line.Text[(at + label.Length + 1)..].Split(' ')[0];
            if (Money().IsMatch(amount)) return Amount(amount);
        }
        throw new InvalidStatementException(NotWealthsimpleMessage);
    }

    /// <summary>"$1,234.56", "-$12.00" or "($12.00)".</summary>
    private static decimal Amount(string text)
    {
        var negative = text.StartsWith('-') || text.StartsWith('(');
        var digits = text.Trim('-', '(', ')', '$').Replace(",", "");
        var value = decimal.Parse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    /// <summary>A page's words as lines, top to bottom, each left to right.</summary>
    private static List<Line> LinesOf(Page page)
    {
        var lines = new List<Line>();
        foreach (var word in page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var line = lines.LastOrDefault();
            if (line is not null && line.Y - word.BoundingBox.Bottom <= SameLine) line.Words.Add(word);
            else lines.Add(new Line(word.BoundingBox.Bottom, [word]));
        }
        foreach (var line in lines) line.Words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        return lines;
    }

    [GeneratedRegex(@"^[-(]?\$-?\d{1,3}(,\d{3})*\.\d{2}\)?$")]
    private static partial Regex Money();

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]{1,15}$")]
    private static partial Regex Code();

    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2}) - (\d{4}-\d{2}-\d{2})")]
    private static partial Regex PeriodPattern();
}

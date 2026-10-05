using System.Globalization;
using System.Text.RegularExpressions;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Finance.Infrastructure.Import;

/// <summary>
/// Neo Financial's monthly PDF statements (Savings, Everyday, Chequing): a summary page with the opening and closing
/// balances, then a table with the transaction date, the posted date, the description, credits, debits (written
/// negative) and the balance after each row. Dates carry no year: it comes from the statement's period. Every row must
/// take the previous balance to its own, and the last one to the closing balance, or the statement is refused rather
/// than imported with a row misread.
/// </summary>
internal static partial class NeoPdfParser
{
    private const string NotNeoMessage =
        "This PDF doesn't look like a Neo Financial statement. Expected a transactions table with Credits, Debits and Balance columns.";

    /// <summary>Words this close vertically are on the same line (their baselines differ by fractions of a point).</summary>
    private const double SameLine = 2;

    /// <summary>A wrapped description continues within this distance below its row.</summary>
    private const double WrappedLine = 16;

    private sealed record Line(double Y, List<Word> Words)
    {
        public string Text => string.Join(' ', Words.Select(w => w.Text));
        public double Left => Words[0].BoundingBox.Left;
    }

    private sealed record Row(DateTime Date, List<string> Description, decimal Amount, decimal Balance, string Text);

    public static bool IsPdf(ReadOnlySpan<byte> bytes) => bytes.StartsWith("%PDF-"u8);

    public static List<Transaction> Parse(byte[] pdf)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdf);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidStatementException("This PDF can't be read. Is it a statement downloaded from Neo, not a scan or a protected file?");
        }

        using (document)
        {
            var pages = document.GetPages().Select(LinesOf).ToList();
            var all = pages.SelectMany(p => p).ToList();
            var (start, end) = Period(all);
            var opening = Summary(all, "Opening balance");
            var closing = Summary(all, "Closing balance");

            var rows = new List<Row>();
            var tables = 0;
            foreach (var lines in pages)
            {
                var header = lines.FindIndex(IsHeader);
                if (header < 0) continue;
                tables++;
                var descriptionLeft = lines[header].Words.First(w => w.Text == "Description").BoundingBox.Left;
                Row? last = null;
                double lastY = 0;
                foreach (var line in lines.Skip(header + 1))
                {
                    if (TryRow(line, start, end, out var row))
                    {
                        rows.Add(row);
                        (last, lastY) = (row, line.Y);
                    }
                    else if (last is not null && lastY - line.Y <= WrappedLine && line.Left >= descriptionLeft - 2
                             && !line.Words.Any(w => Money().IsMatch(w.Text)))
                    {
                        // A description too long for its column goes on below it.
                        last.Description.Add(line.Text);
                        lastY = line.Y;
                    }
                    else
                    {
                        last = null;
                    }
                }
            }
            if (tables == 0) throw new InvalidStatementException(NotNeoMessage);

            Reconcile(rows, opening, closing);
            return rows.Select(row => new Transaction
            {
                Date = row.Date,
                Description = string.Join(' ', row.Description),
                Amount = Math.Abs(row.Amount),
                TransactionType = row.Amount < 0 ? TransactionType.Debit : TransactionType.Credit,
            }).ToList();
        }
    }

    /// <summary>Each row moves the balance by its amount, from the opening balance to the closing one.</summary>
    private static void Reconcile(List<Row> rows, decimal opening, decimal closing)
    {
        var balance = opening;
        foreach (var row in rows)
        {
            if (balance + row.Amount != row.Balance)
            {
                throw new InvalidStatementException(
                    $"Couldn't read this statement reliably: \"{row.Text}\" doesn't take the balance from {balance:N2} to {row.Balance:N2}. Nothing was imported.");
            }
            balance = row.Balance;
        }
        if (balance != closing)
        {
            throw new InvalidStatementException(
                $"Couldn't read this statement reliably: its transactions end at {balance:N2}, but the closing balance is {closing:N2}. Nothing was imported.");
        }
    }

    /// <summary>"Feb 2 Feb 2 Transfer from Everyday 4,000.00 6,408.64": two dates, the description, the amount, the balance.</summary>
    private static bool TryRow(Line line, DateTime start, DateTime end, out Row row)
    {
        row = null!;
        var words = line.Words.Select(w => w.Text).ToList();
        if (words.Count < 7 || !MonthDay(words[0], words[1], out var month, out var day)
                            || !MonthDay(words[2], words[3], out _, out _)
                            || !Money().IsMatch(words[^1]) || !Money().IsMatch(words[^2]))
        {
            return false;
        }
        // The year is the period's: a December row in a statement running into January belongs to the year before.
        var year = month >= start.Month || start.Year == end.Year ? start.Year : end.Year;
        if (day > DateTime.DaysInMonth(year, month)) return false;
        row = new Row(
            new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc),
            [string.Join(' ', words[4..^2])],
            Amount(words[^2]),
            Amount(words[^1]),
            line.Text);
        return true;
    }

    private static bool IsHeader(Line line)
    {
        var words = line.Words.Select(w => w.Text).ToList();
        return words.Contains("Description") && words.Contains("Credits") && words.Contains("Debits") && words.Contains("Balance");
    }

    /// <summary>"Feb 1, 2026 - Feb 28, 2026", by the account's name at the top of each page.</summary>
    private static (DateTime Start, DateTime End) Period(List<Line> lines)
    {
        foreach (var line in lines)
        {
            var match = PeriodPattern().Match(line.Text);
            if (match.Success
                && DateTime.TryParseExact(match.Groups[1].Value, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                && DateTime.TryParseExact(match.Groups[2].Value, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                return (start, end);
            }
        }
        throw new InvalidStatementException(NotNeoMessage);
    }

    private static decimal Summary(List<Line> lines, string label)
    {
        var line = lines.FirstOrDefault(l => l.Text.StartsWith(label, StringComparison.Ordinal) && Money().IsMatch(l.Words[^1].Text))
                   ?? throw new InvalidStatementException(NotNeoMessage);
        return Amount(line.Words[^1].Text);
    }

    private static bool MonthDay(string month, string day, out int monthNumber, out int dayNumber)
    {
        monthNumber = DateTime.TryParseExact(month, "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed.Month : 0;
        dayNumber = int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d is >= 1 and <= 31 ? d : 0;
        return monthNumber > 0 && dayNumber > 0;
    }

    private static decimal Amount(string text) =>
        decimal.Parse(text.Replace(",", ""), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

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

    [GeneratedRegex(@"^-?\d{1,3}(,\d{3})*\.\d{2}$")]
    private static partial Regex Money();

    [GeneratedRegex(@"([A-Z][a-z]{2} \d{1,2}, \d{4}) - ([A-Z][a-z]{2} \d{1,2}, \d{4})")]
    private static partial Regex PeriodPattern();
}

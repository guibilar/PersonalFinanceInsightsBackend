using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace Finance.Infrastructure.Import;

/// <summary>
/// CIBC CSV exports: no header; columns are date (yyyy-MM-dd), description, money out, money in.
/// Read as UTF-8, or as Windows-1252 when the file is not valid UTF-8 (as saved by Excel on Windows).
/// </summary>
public class CibcCsvParser(IOptions<ImportOptions> options) : IStatementParser
{
    private const int DateColumn = 0;
    private const int DescriptionColumn = 1;
    private const int MoneyOutColumn = 2;
    private const int MoneyInColumn = 3;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    private readonly ImportOptions _options = options.Value;

    public List<Transaction> Parse(Stream statement, AccountType account, CategoryMatcher categories)
    {
        var transactions = new List<Transaction>();
        var datedRows = 0;
        using var reader = new StringReader(ReadText(statement));
        // Short rows read as missing fields (null) and stray quotes as data, so any file gets row-by-row checks
        // below instead of an exception halfway through.
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            MissingFieldFound = null,
            BadDataFound = null
        });

        while (csv.Read())
        {
            var description = csv.GetField(DescriptionColumn);
            if (string.IsNullOrEmpty(description) ||
                !DateTime.TryParse(csv.GetField(DateColumn), CultureInfo.InvariantCulture, out var date))
            {
                continue;
            }
            date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            datedRows++;

            if (account == AccountType.Savings)
            {
                // Savings statements: every movement counts, in both directions.
                AddIfAmount(transactions, csv.GetField(MoneyInColumn), date, description, account, TransactionType.Credit, Category.Other, positiveOnly: true);
                AddIfAmount(transactions, csv.GetField(MoneyOutColumn), date, description, account, TransactionType.Debit, Category.Other, positiveOnly: true);
                continue;
            }

            var category = categories.Categorize(description);

            // Income is matched by keyword only; the exclusion list applies to spending.
            if (account == AccountType.Chequing && IsIncome(description))
            {
                AddIfAmount(transactions, csv.GetField(MoneyInColumn), date, description, account, TransactionType.Credit, category);
            }

            if (!IsExcluded(description))
            {
                AddIfAmount(transactions, csv.GetField(MoneyOutColumn), date, description, account, TransactionType.Debit, category);

                // Refunds give money back in the purchase's category; paying the card off is not spending at all.
                if (account == AccountType.CreditCard && !IsCardPayment(description))
                {
                    AddIfAmount(transactions, csv.GetField(MoneyInColumn), date, description, account, TransactionType.Credit, category);
                }
            }
        }

        // Not a single dated row: another bank's export, a spreadsheet, or not a CSV at all. Saying "0 imported"
        // would look like success.
        if (datedRows == 0)
        {
            throw new InvalidStatementException(
                "No transactions found in this file. Is it a CIBC CSV export (date, description, money out, money in)?");
        }

        return transactions;
    }

    private static string ReadText(Stream statement)
    {
        using var buffer = new MemoryStream();
        statement.CopyTo(buffer);
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        if (bytes.StartsWith(Encoding.UTF8.Preamble)) bytes = bytes[Encoding.UTF8.Preamble.Length..];

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Plain UTF-8 decoding would turn accented characters into U+FFFD instead.
            return Windows1252.GetString(bytes);
        }
    }

    private bool IsExcluded(string description) =>
        _options.ExcludedDescriptions.Any(excluded => description.Contains(excluded, StringComparison.Ordinal));

    private bool IsCardPayment(string description) =>
        _options.CardPaymentKeywords.Any(keyword => description.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private bool IsIncome(string description) =>
        _options.IncomeKeywords.Any(keyword => description.Contains(keyword, StringComparison.Ordinal));

    private static void AddIfAmount(List<Transaction> transactions, string? amountField, DateTime date, string description,
        AccountType account, TransactionType type, Category category, bool positiveOnly = false)
    {
        if (!decimal.TryParse(amountField, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            (positiveOnly && amount <= 0))
        {
            return;
        }

        transactions.Add(new Transaction
        {
            Date = date,
            Description = description,
            Amount = amount,
            Category = category,
            TransactionType = type,
            Account = account
        });
    }
}

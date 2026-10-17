using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;

namespace Finance.Infrastructure.Import;

/// <summary>
/// CIBC CSV exports: no header; columns are date (yyyy-MM-dd), description, money out, money in, and on credit card
/// exports the masked card number. Read as UTF-8, or as Windows-1252 when the file is not valid UTF-8 (as saved by
/// Excel on Windows).
/// </summary>
public partial class CibcCsvParser : IStatementParser
{
    private const int DateColumn = 0;
    private const int DescriptionColumn = 1;
    private const int MoneyOutColumn = 2;
    private const int MoneyInColumn = 3;
    private const int CardNumberColumn = 4;

    public StatementFormat Format => StatementFormat.Cibc;

    public ParsedStatement Parse(Stream statement)
    {
        var transactions = new List<Transaction>();
        var datedRows = 0;
        var cardRows = 0;
        using var reader = new StringReader(StatementText.Read(statement));
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
            // Read with this format, a Neo file would come out as no transactions at all, which looks like success.
            if (string.Equals(csv.GetField(DateColumn)?.Trim(), "Transaction Date", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidStatementException(
                    "This file has a header line, as Neo Financial exports do, but this account reads CIBC files. " +
                    "Set the account's statement format to Neo, or choose another account.");
            }
            var description = csv.GetField(DescriptionColumn);
            if (string.IsNullOrEmpty(description) || !StatementText.TryParseDay(csv.GetField(DateColumn), out var date))
            {
                continue;
            }
            datedRows++;
            if (MaskedCardNumber().IsMatch(csv.GetField(CardNumberColumn) ?? "")) cardRows++;

            // Every movement is kept; the flow rules decide later what counts as spending, income or neither.
            AddIfAmount(transactions, csv.GetField(MoneyOutColumn), date, description, TransactionType.Debit);
            AddIfAmount(transactions, csv.GetField(MoneyInColumn), date, description, TransactionType.Credit);
        }

        // Not a single dated row: another bank's export, a spreadsheet, or not a CSV at all. Saying "0 imported"
        // would look like success.
        if (datedRows == 0)
        {
            throw new InvalidStatementException(
                "No transactions found in this file. Is it a CIBC CSV export (date, description, money out, money in)?");
        }

        return new ParsedStatement(transactions, IsCreditCardExport: cardRows > 0);
    }

    private static void AddIfAmount(List<Transaction> transactions, string? amountField, DateTime date, string description,
        TransactionType type)
    {
        if (!StatementText.TryParseAmount(amountField, out var amount) || amount == 0)
        {
            return;
        }

        transactions.Add(new Transaction
        {
            Date = date,
            Description = description,
            Amount = amount,
            TransactionType = type
        });
    }

    // "4500********1234", as in the last column of a CIBC card export.
    [GeneratedRegex(@"^\d{4}\*+\d{3,4}$")]
    private static partial Regex MaskedCardNumber();
}

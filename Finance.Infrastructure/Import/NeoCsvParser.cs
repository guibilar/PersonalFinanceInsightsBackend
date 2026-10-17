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
/// Neo Financial CSV exports (card, chequing and savings): a header line, then one row per movement with the
/// transaction date, an optional status and posted date, the description, and one signed amount: negative is money
/// out, positive is money in. Columns are found by name, so their order doesn't matter. Neo's monthly PDF statements
/// are read too (see <see cref="NeoPdfParser"/>).
/// </summary>
public partial class NeoCsvParser : IStatementParser
{
    private const string DateHeader = "transaction date";
    private const string DescriptionHeader = "description";
    private const string AmountHeader = "amount";
    private const string StatusHeader = "status";

    private const string NotNeoMessage =
        "This file doesn't look like a Neo Financial export. Expected a header line with Transaction Date, Description and Amount.";

    public StatementFormat Format => StatementFormat.Neo;

    public ParsedStatement Parse(Stream statement)
    {
        using var buffer = new MemoryStream();
        statement.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (PdfStatement.IsPdf(bytes)) return NeoPdfParser.Parse(bytes);

        using var reader = new StringReader(StatementText.Read(new MemoryStream(bytes)));
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant()
        });

        if (!csv.Read() || !csv.ReadHeader() || !HasRequiredHeaders(csv.HeaderRecord))
        {
            throw new InvalidStatementException(NotNeoMessage);
        }

        var transactions = new List<Transaction>();
        var datedRows = 0;
        while (csv.Read())
        {
            var description = csv.GetField(DescriptionHeader);
            if (string.IsNullOrWhiteSpace(description) || !StatementText.TryParseDay(csv.GetField(DateHeader), out var date))
            {
                continue;
            }
            datedRows++;

            // A pending row shows up again once it posts, possibly worded differently, so only posted rows count.
            if (csv.TryGetField<string>(StatusHeader, out var status) &&
                !string.IsNullOrWhiteSpace(status) && !status.Trim().Equals("Posted", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!StatementText.TryParseAmount(csv.GetField(AmountHeader), out var amount) || amount == 0)
            {
                continue;
            }

            transactions.Add(new Transaction
            {
                Date = date,
                // Neo pads merchant names and cities with runs of spaces ("CALGARY       CAN").
                Description = WhitespaceRun().Replace(description.Trim(), " "),
                Amount = Math.Abs(amount),
                TransactionType = amount < 0 ? TransactionType.Debit : TransactionType.Credit
            });
        }

        // A header and no dated rows: another export with similar column names, or an empty period.
        if (datedRows == 0)
        {
            throw new InvalidStatementException("No transactions found in this file. Is it a Neo Financial CSV export?");
        }

        return new ParsedStatement(transactions, IsCreditCardExport: false);
    }

    // CsvHelper hands back the header line as written; columns are looked up by their lower-cased names.
    private static bool HasRequiredHeaders(string[]? headers)
    {
        var names = headers?.Select(header => header.Trim().ToLowerInvariant()).ToHashSet();
        return names is not null && new[] { DateHeader, DescriptionHeader, AmountHeader }.All(names.Contains);
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRun();
}

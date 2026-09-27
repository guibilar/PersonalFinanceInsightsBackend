using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Finance.Infrastructure.Import;

/// <summary>
/// CIBC CSV exports: no header; columns are date (yyyy-MM-dd), description, money out, money in.
/// </summary>
public class CibcCsvParser(IOptions<ImportOptions> options) : IStatementParser
{
    private const int DateColumn = 0;
    private const int DescriptionColumn = 1;
    private const int MoneyOutColumn = 2;
    private const int MoneyInColumn = 3;

    private readonly ImportOptions _options = options.Value;

    public List<Transaction> Parse(Stream statement, AccountType account)
    {
        var transactions = new List<Transaction>();
        var statementMonths = new HashSet<DateTime>();
        using var reader = new StreamReader(statement);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false });

        while (csv.Read())
        {
            var description = csv.GetField(DescriptionColumn);
            if (string.IsNullOrEmpty(description) ||
                !DateTime.TryParse(csv.GetField(DateColumn), CultureInfo.InvariantCulture, out var date))
            {
                continue;
            }
            date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
            statementMonths.Add(new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc));

            if (account == AccountType.Savings)
            {
                // Savings statements: every movement counts, in both directions.
                AddIfAmount(transactions, csv.GetField(MoneyInColumn), date, description, account, TransactionType.Credit, Category.Other, positiveOnly: true);
                AddIfAmount(transactions, csv.GetField(MoneyOutColumn), date, description, account, TransactionType.Debit, Category.Other, positiveOnly: true);
                continue;
            }

            var category = TransactionCategorizer.Categorize(description, _options);

            // Income is matched by keyword only; the exclusion list applies to spending.
            if (account == AccountType.Chequing && IsIncome(description))
            {
                AddIfAmount(transactions, csv.GetField(MoneyInColumn), date, description, account, TransactionType.Credit, category);
            }

            var replacedByMonthlyRent = _options.MonthlyRent != null && category == Category.Rent;
            if (!replacedByMonthlyRent && !IsExcluded(description))
            {
                AddIfAmount(transactions, csv.GetField(MoneyOutColumn), date, description, account, TransactionType.Debit, category);
            }
        }

        if (account == AccountType.Chequing && _options.MonthlyRent is { } rent)
        {
            AddMonthlyRent(transactions, rent, account, statementMonths);
        }

        return transactions;
    }

    private bool IsExcluded(string description) =>
        _options.ExcludedDescriptions.Any(excluded => description.Contains(excluded, StringComparison.Ordinal));

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

    // One rent per month the statement covers (every dated row counts, including the skipped rent itself).
    private static void AddMonthlyRent(List<Transaction> transactions, MonthlyRentOptions rent, AccountType account,
        IEnumerable<DateTime> statementMonths)
    {
        foreach (var month in statementMonths.Order())
        {
            transactions.Add(new Transaction
            {
                Date = month,
                Description = rent.Description,
                Amount = rent.Amount,
                Category = Category.Rent,
                TransactionType = TransactionType.Debit,
                Account = account
            });
        }
    }
}

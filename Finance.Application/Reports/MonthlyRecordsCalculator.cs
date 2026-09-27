
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Reports;

public static class MonthlyRecordsCalculator
{
    /// <summary>Totals per calendar month, overall and per category, ordered by month.</summary>
    public static List<MonthlyRecords> GenerateMonthlyRecords(List<Transaction> records) => Group(records, r => r.Amount);

    /// <summary>
    /// Spending per calendar month: purchases count positive and refunds (credits) negative, so a refunded
    /// purchase nets out in its category.
    /// </summary>
    public static List<MonthlyRecords> GenerateSpendingRecords(List<Transaction> records) =>
        Group(records, r => r.TransactionType == TransactionType.Credit ? -r.Amount : r.Amount);

    private static List<MonthlyRecords> Group(List<Transaction> records, Func<Transaction, decimal> amount)
    {
        return records
            .GroupBy(r => new { r.Date.Year, r.Date.Month })
            .Select(g => new MonthlyRecords
            {
                YearMonth = new DateTime(g.Key.Year, g.Key.Month, 1),
                Categories = g.GroupBy(r => r.Category)
                    .ToDictionary(c => c.Key, c => c.Sum(amount)),
                Total = g.Sum(amount)
            })
            .OrderBy(x => x.YearMonth)
            .ToList();
    }
}

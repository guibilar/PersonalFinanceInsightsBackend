
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Reports;

public static class MonthlyRecordsCalculator
{
    /// <summary>Totals per calendar month, overall and per category, ordered by month.</summary>
    public static List<MonthlyRecords> GenerateMonthlyRecords(List<Transaction> records) => Group(records, r => r.Amount);

    /// <summary>
    /// Spending per calendar month from spending and refund transactions (anything else is left out): purchases count
    /// positive and refunds negative, so a refunded purchase nets out in its category.
    /// </summary>
    public static List<MonthlyRecords> GenerateSpendingRecords(List<Transaction> records) =>
        Group(records.Where(r => r.Flow is Flow.Spending or Flow.Refund).ToList(), SpendingAmount);

    /// <summary>What a transaction adds to spending: its amount, negative for a refund, and 0 unless spending or refund.</summary>
    public static decimal SpendingAmount(Transaction transaction) => transaction.Flow switch
    {
        Flow.Spending => transaction.Amount,
        Flow.Refund => -transaction.Amount,
        _ => 0
    };

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


using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Reports;

public static class MonthlyRecordsCalculator
{
    /// <summary>Totals per calendar month, overall and per category, ordered by month.</summary>
    public static List<MonthlyRecords> GenerateMonthlyRecords(List<Transaction> records)
    {
        return records
            .GroupBy(r => new { r.Date.Year, r.Date.Month })
            .Select(g => new MonthlyRecords
            {
                YearMonth = new DateTime(g.Key.Year, g.Key.Month, 1),
                Categories = g.GroupBy(r => r.Category)
                    .ToDictionary(c => c.Key, c => c.Sum(r => r.Amount)),
                Total = g.Sum(r => r.Amount)
            })
            .OrderBy(x => x.YearMonth)
            .ToList();
    }
}

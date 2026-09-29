using System.Globalization;
using Finance.Contracts.Reports;

namespace Finance.Application.Reports;

/// <summary>Calendar periods of a <see cref="BreakdownInterval"/>. Dates are calendar dates (midnight UTC); weeks start on Monday.</summary>
public static class BreakdownPeriods
{
    /// <summary>The first day of the period that contains <paramref name="date"/>.</summary>
    public static DateTime StartOf(DateTime date, BreakdownInterval interval)
    {
        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        return interval switch
        {
            BreakdownInterval.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            BreakdownInterval.Month => new DateTime(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            BreakdownInterval.Quarter => new DateTime(day.Year, (day.Month - 1) / 3 * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(day.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    /// <summary>The first day of the next period, for a period start.</summary>
    public static DateTime Next(DateTime start, BreakdownInterval interval) => interval switch
    {
        BreakdownInterval.Week => start.AddDays(7),
        BreakdownInterval.Month => start.AddMonths(1),
        BreakdownInterval.Quarter => start.AddMonths(3),
        _ => start.AddYears(1),
    };

    public static string Label(DateTime start, BreakdownInterval interval) => interval switch
    {
        BreakdownInterval.Week => start.ToString("MMM d", CultureInfo.InvariantCulture),
        BreakdownInterval.Month => start.ToString("MMM yyyy", CultureInfo.InvariantCulture),
        BreakdownInterval.Quarter => $"Q{(start.Month - 1) / 3 + 1} {start.Year}",
        _ => start.Year.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>How many periods run from the one containing <paramref name="first"/> to the one containing <paramref name="last"/>.</summary>
    public static int Count(DateTime first, DateTime last, BreakdownInterval interval)
    {
        var count = 0;
        for (var start = StartOf(first, interval); start <= last; start = Next(start, interval)) count++;
        return count;
    }
}

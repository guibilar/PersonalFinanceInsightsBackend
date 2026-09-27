using Microsoft.Extensions.Options;

namespace Finance.Application.Reports;

/// <summary>
/// "Today" and "this month" in the configured time zone (<see cref="ReportOptions.TimeZone"/>). Transactions are
/// stored as calendar dates at midnight UTC, so results are expressed the same way.
/// </summary>
public class ReportingCalendar(TimeProvider timeProvider, IOptions<ReportOptions> options)
{
    private readonly TimeZoneInfo _timeZone = options.Value.GetTimeZone();

    public DateTime Today()
    {
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), _timeZone);
        return new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Utc);
    }

    public DateTime CurrentMonthStart()
    {
        var today = Today();
        return new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}

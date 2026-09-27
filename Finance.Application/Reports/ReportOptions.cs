namespace Finance.Application.Reports;

/// <summary>Settings for the reports (section "Reports").</summary>
public class ReportOptions
{
    public const string SectionName = "Reports";

    /// <summary>
    /// IANA time zone (e.g. "America/Toronto") that decides which day, and so which month and year, it is now.
    /// Without it the current month would roll over at midnight UTC, i.e. in the evening of the last local day.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";

    public TimeZoneInfo GetTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);

    public bool IsValid() => TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _);
}

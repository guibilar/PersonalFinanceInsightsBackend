using System;
using System.Collections.Generic;
using Finance.Domain.Enums;

namespace Finance.Contracts.Reports;

public class MonthlyCategorizedGraphData
{
    public List<Category> Categories { get; set; } = [];
    public List<MonthDataPoint> MonthlyData { get; set; } = [];
    public decimal AverageMonthlyTotal { get; set; }

    /// <summary>
    /// Current-month report only: spending in the previous month up to the same day of the month (the whole
    /// month when it is shorter), so a month in progress is compared like for like. Null in the year report.
    /// </summary>
    public decimal? PreviousMonthToDateTotal { get; set; }
}

public class MonthDataPoint
{
    public string Month { get; set; } = string.Empty;
    public Dictionary<Category, decimal> CategoryValues { get; set; } = [];
    public decimal Total { get; set; }
}

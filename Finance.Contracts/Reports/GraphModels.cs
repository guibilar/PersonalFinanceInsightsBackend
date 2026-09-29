using System;
using System.Collections.Generic;
using Finance.Contracts.Outputs;

namespace Finance.Contracts.Reports;

public class MonthlyCategorizedGraphData
{
    /// <summary>The categories with spending in the report, by name, with their colours for the charts.</summary>
    public List<CategoryReferenceDto> Categories { get; set; } = [];
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
    /// <summary>Spending per category, by category name.</summary>
    public Dictionary<string, decimal> CategoryValues { get; set; } = [];
    public decimal Total { get; set; }
}

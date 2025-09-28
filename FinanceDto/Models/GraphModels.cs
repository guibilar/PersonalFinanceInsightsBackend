using System;
using System.Collections.Generic;

namespace FinanceDto.Models;

public class MonthlyCategorizedGraphData
{
    public List<string> Categories { get; set; } = [];
    public List<MonthDataPoint> MonthlyData { get; set; } = [];
    public decimal AverageMonthlyTotal { get; set; }
}

public class MonthDataPoint
{
    public string Month { get; set; } = string.Empty;
    public Dictionary<string, decimal> CategoryValues { get; set; } = [];
    public decimal Total { get; set; }
}

using System;
using System.Collections.Generic;
using FinanceDto.Enums;

namespace FinanceDto.Models;

public class MonthlyCategorizedGraphData
{
    public List<Category> Categories { get; set; } = [];
    public List<MonthDataPoint> MonthlyData { get; set; } = [];
    public decimal AverageMonthlyTotal { get; set; }
}

public class MonthDataPoint
{
    public string Month { get; set; } = string.Empty;
    public Dictionary<Category, decimal> CategoryValues { get; set; } = [];
    public decimal Total { get; set; }
}

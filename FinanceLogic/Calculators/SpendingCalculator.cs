using System.Globalization;
using ClosedXML.Excel;
using CrossCutting.Helpers;
using FinanceDomain.Models;
using FinanceDto.Enums;
using FinanceDto.Models;

namespace CrossCutting.Calculators
{
    public static class SpendingCalculator
    {
        public static void GenerateExcelReport(List<Transaction> records, List<MonthlyRecords> monthly, List<Category> categories, string fileName)
        {
            using (var workbook = new XLWorkbook())
            {
                // --- Sheet 1: Monthly totals by category --
                var totalsSheet = workbook.Worksheets.Add("Monthly Spending Totals");

                // Header
                totalsSheet.Cell(1, 1).Value = "Month";
                foreach (Category category in Enum.GetValues(typeof(Category)))
                {
                    totalsSheet.Cell(1, (int)category + 2).Value = category.ToString();
                }
                totalsSheet.Cell(1, Enum.GetValues(typeof(Category)).Length + 2).Value = "Total";

                // Monthly totals
                int row = 2;
                foreach (var m in monthly)
                {
                    totalsSheet.Cell(row, 1).Value = m.YearMonth.ToString("yyyy-MM");

                    foreach (Category category in Enum.GetValues(typeof(Category)))
                    {
                        var value = m.Categories.TryGetValue(category, out var v) ? v : 0m;
                        totalsSheet.Cell(row, (int)category + 2).Value = value;
                        totalsSheet.Cell(row, (int)category + 2).Style.NumberFormat.Format = "$#,##0.00";
                    }

                    totalsSheet.Cell(row, Enum.GetValues(typeof(Category)).Length + 2).Value = m.Total;
                    totalsSheet.Cell(row, Enum.GetValues(typeof(Category)).Length + 2).Style.NumberFormat.Format = "$#,##0.00";

                    row++;
                }

                row++;

                // Averages
                totalsSheet.Cell(row, 1).Value = "Average";
                for (int i = 0; i < categories.Count; i++)
                {
                    double avg = monthly.Average(m => m.Categories.TryGetValue(categories[i], out var v) ? (double)v : 0.0);
                    totalsSheet.Cell(row, i + 2).Value = avg;
                    totalsSheet.Cell(row, i + 2).Style.NumberFormat.Format = "$#,##0.00";
                }
                totalsSheet.Cell(row, categories.Count + 2).Value = monthly.Average(m => (double)m.Total);
                totalsSheet.Cell(row, categories.Count + 2).Style.NumberFormat.Format = "$#,##0.00";

                totalsSheet.Columns().AdjustToContents();
                totalsSheet.Row(1).Style.Font.Bold = true;

                // --- Additional Sheets: One per month with totals by description ---
                foreach (var m in monthly)
                {
                    var ws = workbook.Worksheets.Add(m.YearMonth.ToString("MMMM yyyy"));

                    ws.Cell(1, 1).Value = "Description";
                    ws.Cell(1, 2).Value = "Total Amount";
                    ws.Cell(1, 3).Value = "Category";
                    ws.Row(1).Style.Font.Bold = true;

                    int detailRow = 2;

                    // Group that month's records by Description
                    var descGroups = records
                        .Where(r => r.Date.Year == m.YearMonth.Year && r.Date.Month == m.YearMonth.Month)
                        .GroupBy(r => new { r.Description, r.Category })
                        .OrderBy(g => g.Key.Description);

                    foreach (var g in descGroups)
                    {
                        ws.Cell(detailRow, 1).Value = g.Key.Description;
                        ws.Cell(detailRow, 2).Value = g.Sum(r => r.Amount);
                        ws.Cell(detailRow, 2).Style.NumberFormat.Format = "$#,##0.00";
                        ws.Cell(detailRow, 3).Value = g.Key.Category.ToString();

                        detailRow++;
                    }

                    ws.Columns().AdjustToContents();
                }
                
                var filePath = FileHelper.GetResultsPathForFile(fileName, "xlsx");

                workbook.SaveAs(filePath);
            }

            Console.WriteLine($"{fileName}.xlsx saved (totals by category + totals by description per month).");
        }

        public static void GenerateMonthlyCategorizedGraph(List<MonthlyRecords> monthly, double avgMonthlyTotal, List<Category> categories, string fileName)
        {
            var plt2 = new ScottPlot.Plot();

            var palette = new ScottPlot.Palettes.Category10();
            var barsList = new List<ScottPlot.Bar>();

            foreach (var (monthIndex, monthlyRecord) in monthly.Select((record, index) => (index, record)))
            {
                int pos = monthIndex + 1;
                double nextBarBase = 0.0;

                foreach (Category category in Enum.GetValues(typeof(Category)))
                {
                    double value = 0;
                    if (monthlyRecord.Categories.TryGetValue(category, out decimal d))
                        value = (double)d;

                    if (value == 0)
                        continue;

                    var bar = new ScottPlot.Bar()
                    {
                        Position = pos,
                        ValueBase = nextBarBase,
                        Value = nextBarBase + value,
                        FillColor = palette.GetColor((int)category)
                    };

                    barsList.Add(bar);
                    nextBarBase += value;
                }
            }

            var barPlot = plt2.Add.Bars(barsList.ToArray());

            barPlot.ValueLabelStyle.IsVisible = false;

            var ticks = new ScottPlot.Tick[monthly.Count];
            for (int i = 0; i < monthly.Count; i++)
            {
                int pos = i + 1;
                string label = monthly[i].YearMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture);
                ticks[i] = new ScottPlot.Tick(pos, label);
            }
            plt2.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);
            plt2.Axes.Bottom.MajorTickStyle.Length = 90;
            plt2.Axes.Bottom.TickLabelStyle.Alignment = ScottPlot.Alignment.LowerRight;
            plt2.Axes.Bottom.TickLabelStyle.Rotation = 90;

            plt2.Legend.IsVisible = true;
            plt2.Legend.ManualItems.Clear();

            for (int i = 0; i < categories.Count; i++)
            {
                plt2.Legend.ManualItems.Add(new ScottPlot.LegendItem()
                {
                    LabelText = categories[i].ToString(),
                    FillColor = palette.GetColor(i),
                    LineWidth = 0
                });
            }

            plt2.Legend.Orientation = ScottPlot.Orientation.Horizontal;
            plt2.Legend.Alignment = ScottPlot.Alignment.UpperRight;

            plt2.Title("Monthly Spending by Category (Stacked)");
            plt2.YLabel("Amount ($)");
            plt2.Axes.Margins(bottom: 0, top: .25);
            plt2.Grid.MajorLineWidth = 1;
            plt2.Grid.IsVisible = true;

            // === Add Average Line to Stacked Bar Chart ===
            var avgBarLine = plt2.Add.HorizontalLine(avgMonthlyTotal);
            avgBarLine.LinePattern = ScottPlot.LinePattern.Dashed;
            avgBarLine.LineWidth = 2;
            avgBarLine.Color = ScottPlot.Colors.Red;
            avgBarLine.LegendText = $"Avg Spending: {avgMonthlyTotal:0.00}";
            

            var filePath = FileHelper.GetResultsPathForFile(fileName, "png");
            plt2.SavePng(filePath, 900, 600);

            Console.WriteLine($"{fileName}.png saved (stacked bars).");
        }

        public static double GenerateMonthlyTotalsGraph(List<MonthlyRecords> monthlyExpenses, double avgMonthlyTotal, List<MonthlyRecords> monthlyIncome, string fileName)
        {
            var plt1 = new ScottPlot.Plot();

            double[] xs = [.. monthlyExpenses.Select(m => m.YearMonth.ToOADate())];
            double[] ys = [.. monthlyExpenses.Select(m => (double)m.Total)];

            var scatter = plt1.Add.Scatter(xs, ys);
            for (int i = 0; i < xs.Length; i++)
            {
                plt1.Add.Text(
                    ys[i].ToString("$0.00"),
                    xs[i] + 5,
                    ys[i] + 20
                );
            }

            scatter.LegendText = "Total Spending";
            scatter.MarkerSize = 5;

            double[] xsIncome = [.. monthlyIncome.Select(m => m.YearMonth.ToOADate())];
            double[] ysIncome = [.. monthlyIncome.Select(m => (double)m.Total)];

            for (int i = 0; i < xs.Length; i++)
            {
                plt1.Add.Text(
                    ysIncome[i].ToString("$0.00"),
                    xsIncome[i] + 5,
                    ysIncome[i] + 20
                );
            }

            var scatter2 = plt1.Add.Scatter(xsIncome, ysIncome);
            scatter2.LegendText = "Total Income";
            scatter2.MarkerSize = 5;

            plt1.Legend.Orientation = ScottPlot.Orientation.Vertical;
            plt1.Legend.Alignment = ScottPlot.Alignment.UpperRight;

            plt1.Axes.DateTimeTicksBottom();
            plt1.Title("Monthly Spending Trend");
            plt1.YLabel("Amount ($)");
            plt1.ShowLegend();

            // === Add Average Line to Line Chart ===
            var avgLine = plt1.Add.HorizontalLine(avgMonthlyTotal);
            avgLine.LinePattern = ScottPlot.LinePattern.Dashed;
            avgLine.LineWidth = 2;
            avgLine.Color = ScottPlot.Colors.Red;
            avgLine.LegendText = $"Average Spending: {avgMonthlyTotal:0.00}";

            var filePath = FileHelper.GetResultsPathForFile(fileName, "png");
            plt1.SavePng(filePath, 900, 600);            
            Console.WriteLine($"{fileName}.png saved (line chart).");
            return avgMonthlyTotal;
        }

        public static List<MonthlyRecords> GenerateMonthlyRecords(List<Transaction> records)
        {
            return records
                .GroupBy(r => new { Year = r.Date.Year, Month = r.Date.Month })
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
}
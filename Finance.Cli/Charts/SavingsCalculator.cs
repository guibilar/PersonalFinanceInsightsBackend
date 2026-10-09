using System.Globalization;
using Finance.Application.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Cli.Charts;

public abstract class SavingsCalculator
{
    public static List<MonthlyRecords> GenerateMonthlySavingsRecords(List<Transaction> records)
    {
        return records
            .GroupBy(r => new { Year = r.Date.Year, Month = r.Date.Month })
            .Select(g => new MonthlyRecords
            {
                YearMonth = new DateTime(g.Key.Year, g.Key.Month, 1),
                Categories = g.GroupBy(r => r.CategoryId)
                              .ToDictionary(c => c.Key, c => c.Where(x => x.TransactionType == TransactionType.Credit).Sum(r => r.Amount) - c.Where(x => x.TransactionType == TransactionType.Debit).Sum(r => r.Amount)),
                Total = g.Where(x => x.TransactionType == TransactionType.Credit).Sum(r => r.Amount) - g.Where(x => x.TransactionType == TransactionType.Debit).Sum(r => r.Amount)
            })
            .OrderBy(x => x.YearMonth)
            .ToList();
    }        

            public static void GenerateMonthlyTotalsSavingsGraph(List<MonthlyRecords> monthlySavings, string fileName)
    {
        if (monthlySavings.Count == 0)
        {
            Console.WriteLine($"No savings transactions; {fileName}.png not drawn.");
            return;
        }

        var plt1 = new ScottPlot.Plot();

        double[] xs = [.. monthlySavings.Select(m => m.YearMonth.ToOADate())];
        double[] ys = [.. monthlySavings.Select(m => (double)m.Total)];

        var scatter = plt1.Add.Scatter(xs, ys);
        scatter.LegendText = "Total Savings";
        scatter.MarkerSize = 5;
        scatter.LineWidth = 2;
        scatter.Color = ScottPlot.Colors.Green;

        // Each value at its point, a few pixels above it (below for a negative month), whatever the amounts' scale.
        for (int i = 0; i < xs.Length; i++)
        {
            var label = plt1.Add.Text(ys[i].ToString("$#,##0.00;-$#,##0.00", CultureInfo.InvariantCulture), xs[i], ys[i]);
            var below = ys[i] < 0;
            label.LabelAlignment = below ? ScottPlot.Alignment.UpperCenter : ScottPlot.Alignment.LowerCenter;
            label.LabelOffsetY = below ? 8 : -8;
        }

        // Custom ticks aligned with data
        var ticks = new ScottPlot.Tick[monthlySavings.Count];
        for (int i = 0; i < monthlySavings.Count; i++)
        {
            string label = monthlySavings[i].YearMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture);
            ticks[i] = new ScottPlot.Tick(xs[i], label);
        }
        plt1.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks);

        // Style bottom axis ticks
        plt1.Axes.Bottom.TickLabelStyle.Rotation = 90;
        plt1.Axes.Bottom.TickLabelStyle.OffsetY = 30;
        plt1.Axes.Bottom.TickLabelStyle.Alignment = ScottPlot.Alignment.LowerCenter;

        // Ensure enough space for rotated labels
        plt1.Axes.Bottom.MinimumSize = 100;

        // From zero (or the lowest month, when one is negative) to the highest, with room for the labels: months that
        // all saved the same would otherwise be zoomed into a sliver around that amount.
        var low = Math.Min(0, ys.Min());
        var high = Math.Max(0, ys.Max());
        var pad = high > low ? (high - low) * 0.15 : 1;
        plt1.Axes.SetLimitsY(low < 0 ? low - pad : 0, high + pad);

        // Labels & title
        plt1.Title("Monthly Savings Trend");
        plt1.YLabel("Amount ($)");
        plt1.ShowLegend();

        // Save higher resolution for readability
        plt1.SavePng(FileHelper.GetResultsPathForFile(fileName, "png"), 1200, 800);
        Console.WriteLine($"{fileName}.png saved (line chart).");
    }

}

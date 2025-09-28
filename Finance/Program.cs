using CrossCutting.Calculators;
using CrossCutting.Helpers;

namespace Finance;

class Program
{
    static void Main()
    {
        var records = ReaderHelper.ReadRecords(includeRent: true);
        var recordsWithOutRent = ReaderHelper.ReadRecords(includeRent: false);
        var recordsSavings = ReaderHelper.ReadSavings();

        // Group totals per month/category
        var monthly = SpendingCalculator.GenerateMonthlyRecords(records);
        var monthlyWithOutRent = SpendingCalculator.GenerateMonthlyRecords(recordsWithOutRent);
        var monthlySavings = SavingsCalculator.GenerateMonthlySavingsRecords(recordsSavings);

        // ================= Line Chart: Monthly Totals =================
        var avgMonthlyTotal = monthly.Average(m => (double)m.Total);
        var avgMonthlyTotalWithOutRent = monthlyWithOutRent.Average(m => (double)m.Total);

        var income = SpendingCalculator.GenerateMonthlyRecords(ReaderHelper.ReadIncome());

        SpendingCalculator.GenerateMonthlyTotalsGraph(monthly, avgMonthlyTotal, income, "MonthlyTrend");
        SpendingCalculator.GenerateMonthlyTotalsGraph(monthlyWithOutRent, avgMonthlyTotalWithOutRent, income, "MonthlyTrendWithoutRent");

        var categories = records.Select(r => r.Category).Distinct().OrderBy(c => c).ToList();
        var categoriesWithOutRent = recordsWithOutRent.Select(r => r.Category).Distinct().OrderBy(c => c).ToList();

        // ================= Savings Chart: Savings Totals =================
        SavingsCalculator.GenerateMonthlyTotalsSavingsGraph(monthlySavings, "MonthlySavingsTrend");

        // ================= Stacked Bar Chart: Categories =================
        SpendingCalculator.GenerateMonthlyCategorizedGraph(monthly, avgMonthlyTotal, categories, "CategoryBreakdown");
        SpendingCalculator.GenerateMonthlyCategorizedGraph(monthlyWithOutRent, avgMonthlyTotalWithOutRent, categoriesWithOutRent, "CategoryBreakdownWithoutRent");


        // ================= Export Monthly Breakdown to Excel =================
        SpendingCalculator.GenerateExcelReport(recordsWithOutRent, monthlyWithOutRent, categoriesWithOutRent, "MonthlySpendingBreakdown");
    }
}
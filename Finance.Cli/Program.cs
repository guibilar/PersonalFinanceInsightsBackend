using Finance.Application.Reports;
using Finance.Cli.Charts;
using Finance.Domain.Enums;
using Finance.Application.Import;
using Finance.Domain.Entities;
using Finance.Infrastructure;
using Finance.Infrastructure.Import;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Finance.Cli;

class Program
{
    static void Main()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(DependencyInjection.ImportRulesFile)
            .Build();
        var options = configuration.GetSection(ImportOptions.SectionName).Get<ImportOptions>() ?? new ImportOptions();
        var parser = new CibcCsvParser(Options.Create(options));

        List<Transaction> Read(string file, AccountType account)
        {
            using var stream = File.OpenRead(FileHelper.GetInputPathForFile(file, "CIBC", "csv"));
            return parser.Parse(stream, account);
        }

        var creditCard = Read("cibc_credit", AccountType.CreditCard);
        var chequing = Read("cibc_debit", AccountType.Chequing);

        var records = creditCard.Concat(chequing).Where(t => t.TransactionType == TransactionType.Debit).ToList();
        var recordsWithOutRent = records.Where(t => t.Category != Category.Rent).ToList();
        var recordsIncome = chequing.Where(t => t.TransactionType == TransactionType.Credit).ToList();
        var recordsSavings = Read("cibc_savings", AccountType.Savings);

        // Group totals per month/category
        var monthly = MonthlyRecordsCalculator.GenerateMonthlyRecords(records);
        var monthlyWithOutRent = MonthlyRecordsCalculator.GenerateMonthlyRecords(recordsWithOutRent);
        var monthlySavings = SavingsCalculator.GenerateMonthlySavingsRecords(recordsSavings);

        // ================= Line Chart: Monthly Totals =================
        var avgMonthlyTotal = monthly.Average(m => (double)m.Total);
        var avgMonthlyTotalWithOutRent = monthlyWithOutRent.Average(m => (double)m.Total);

        var income = MonthlyRecordsCalculator.GenerateMonthlyRecords(recordsIncome);

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
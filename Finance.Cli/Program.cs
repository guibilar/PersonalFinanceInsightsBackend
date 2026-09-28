using Finance.Application.Reports;
using Finance.Cli.Charts;
using Finance.Domain.Enums;
using Finance.Application.Import;
using Finance.Domain.Entities;
using Finance.Domain.Exceptions;
using Finance.Infrastructure;
using Finance.Infrastructure.Import;
using Microsoft.Extensions.Configuration;

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
        var parser = new CibcCsvParser();
        // No database here: the rules in import-rules.json, not the ones edited in the app.
        var classifier = new TransactionClassifier(
            new CategoryMatcher(options.ToCategoryRules()), new FlowMatcher(options.ToFlowRules()));

        List<Transaction> Read(string file, AccountType account)
        {
            using var stream = File.OpenRead(FileHelper.GetInputPathForFile(file, "CIBC", "csv"));
            try
            {
                var transactions = parser.Parse(stream).Transactions;
                transactions.ForEach(t => classifier.Classify(t, account));
                return transactions;
            }
            catch (InvalidStatementException ex)
            {
                // An empty export for one account shouldn't stop the report for the others.
                Console.WriteLine($"{file}.csv: {ex.Message}");
                return [];
            }
        }

        var creditCard = Read("cibc_credit", AccountType.CreditCard);
        var chequing = Read("cibc_debit", AccountType.Chequing);

        if (creditCard.Count == 0 && chequing.Count == 0)
        {
            Console.WriteLine("No transactions found in cibc_credit.csv or cibc_debit.csv; nothing to report.");
            return;
        }

        var recordsSavings = Read("cibc_savings", AccountType.Savings);
        var all = creditCard.Concat(chequing).Concat(recordsSavings).ToList();
        // Spending: purchases minus refunds, whichever account (transfers and income stay out).
        var records = all.Where(t => t.Flow is Flow.Spending or Flow.Refund).ToList();
        var recordsWithOutRent = records.Where(t => t.Category != Category.Rent).ToList();
        var recordsIncome = all.Where(t => t.Flow == Flow.Income).ToList();

        // Group totals per month/category
        var monthly = MonthlyRecordsCalculator.GenerateSpendingRecords(records);
        var monthlyWithOutRent = MonthlyRecordsCalculator.GenerateSpendingRecords(recordsWithOutRent);
        var monthlySavings = SavingsCalculator.GenerateMonthlySavingsRecords(recordsSavings.Where(t => t.Flow != Flow.Ignored).ToList());

        // ================= Line Chart: Monthly Totals =================
        // Empty when a statement only has rent (or nothing but income).
        var avgMonthlyTotal = monthly.Count > 0 ? monthly.Average(m => (double)m.Total) : 0;
        var avgMonthlyTotalWithOutRent = monthlyWithOutRent.Count > 0 ? monthlyWithOutRent.Average(m => (double)m.Total) : 0;

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
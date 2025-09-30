using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using FinanceDomain.Models;
using FinanceDto.Enums;

namespace CrossCutting.Helpers
{
    public static class ReaderHelper
    {
        public static List<Transaction> ReadRecords(bool includeRent = false)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,
            };

            var filePathCredit = FileHelper.GetInputPathForFile("cibc_credit", "CIBC", "csv");
            var filePathDebit = FileHelper.GetInputPathForFile("cibc_debit", "CIBC", "csv");


            var records = new List<Transaction>();
            var readerCredit = new StreamReader(filePathCredit);
            var csvCredit = new CsvReader(readerCredit, config);

            var readerDebit = new StreamReader(filePathDebit);
            var csvDebit = new CsvReader(readerDebit, config);

            records = ReadCreditRecords(records, csvCredit, AccountType.CreditCard, includeRent);
            records = ReadCreditRecords(records, csvDebit, AccountType.Chequing, includeRent);
            return records;
        }

        public static List<Transaction> ReadIncome()
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,
            };

            var filePathDebit = FileHelper.GetInputPathForFile("cibc_debit", "CIBC", "csv");
            
            var records = new List<Transaction>();

            var readerDebit = new StreamReader(filePathDebit);
            var csvDebit = new CsvReader(readerDebit, config);

            records = ReadIncomeRecords(records, AccountType.Chequing, csvDebit);
            return records;
        }

        public static List<Transaction> ReadSavings()
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,
            };

            var filePathSavings = FileHelper.GetInputPathForFile("cibc_savings", "CIBC", "csv");

            var records = new List<Transaction>();

            var readerSavings = new StreamReader(filePathSavings);
            var csvSavings = new CsvReader(readerSavings, config);

            records = ReadSavingsRecords(records, csvSavings);
            return records;
        }

        private static List<Transaction> ReadCreditRecords(List<Transaction> records, CsvReader csv, AccountType account, bool includeRent = false)
        {
            while (csv.Read())
            {
                var dateStr = csv.GetField(0);
                var desc = csv.GetField(1);
                var amountStr = csv.GetField(2);

                if (DateTime.TryParse(dateStr, out DateTime date) &&
                    decimal.TryParse(amountStr, out decimal amount) &&
                    !string.IsNullOrEmpty(desc) &&
                    !desc.Contains("Internet Banking INTERNET TRANSFER") &&
                    !desc.Contains("Internet Banking FULFILL REQUEST") &&
                    !desc.Contains("UBER* PENDING"))
                {
                    records.Add(new Transaction
                    {
                        Date = date,
                        Description = desc,
                        Amount = amount,
                        Category = CategorizationHelper.Categorize(desc),
                        Account = account,
                        TransactionType = TransactionTypeHelper.DefineTransactionType(2)
                    });
                }
            }

            records = [.. records.Where(x => x.Category != Category.Rent)];

            if (!includeRent)
                return records;

            var rentMonths = records
                .Select(r => new { r.Date.Year, r.Date.Month })
                .Distinct().ToList();

            foreach (var m in rentMonths)
            {
                records.Add(new Transaction
                {
                    Date = new DateTime(m.Year, m.Month, 1),
                    Description = "Monthly Rent",
                    Amount = 1947.50m,
                    Category = Category.Rent,
                    Account = account,
                    TransactionType = TransactionTypeHelper.DefineTransactionType(2)
                });
            }
            return records;
        }

        private static List<Transaction> ReadIncomeRecords(List<Transaction> records, AccountType account, CsvReader csv)
        {
            while (csv.Read())
            {
                var dateStr = csv.GetField(0);
                var desc = csv.GetField(1);
                var amountStr = csv.GetField(3);

                if (DateTime.TryParse(dateStr, out DateTime date) &&
                    decimal.TryParse(amountStr, out decimal amount) &&
                    !string.IsNullOrEmpty(desc) &&
                    (desc.Contains("Deel Canada") || desc.Contains("DEPOSIT TrupCAN")))
                {
                    records.Add(new Transaction
                    {
                        Date = date,
                        Description = desc,
                        Amount = amount,
                        Category = CategorizationHelper.Categorize(desc),
                        TransactionType = TransactionTypeHelper.DefineTransactionType(3),
                        Account = account
                    });
                }
            }
            return records;
        }

        private static List<Transaction> ReadSavingsRecords(List<Transaction> records, CsvReader csv)
        {
            while (csv.Read())
            {
                var dateStr = csv.GetField(0);
                var desc = csv.GetField(1);
                var amountStrDebit = csv.GetField(2);
                var amountStrCredit = csv.GetField(3);

                DateTime.TryParse(dateStr, out DateTime date);

                if (string.IsNullOrEmpty(desc))
                {
                    throw new ArgumentException("No description");
                }

                if (decimal.TryParse(amountStrCredit, out decimal amountCredit) && amountCredit > 0)
                {
                    records.Add(new Transaction
                    {
                        Date = date,
                        Description = desc,
                        Amount = amountCredit,
                        TransactionType = TransactionType.Credit,
                        Account = AccountType.Savings
                    });
                }

                if (decimal.TryParse(amountStrDebit, out decimal amountDebit) && amountDebit > 0)
                {
                    records.Add(new Transaction
                    {
                        Date = date,
                        Description = desc,
                        Amount = amountDebit,
                        TransactionType = TransactionType.Debit,
                        Account = AccountType.Savings
                    });
                }
            }
            return records;
        }

    }
}
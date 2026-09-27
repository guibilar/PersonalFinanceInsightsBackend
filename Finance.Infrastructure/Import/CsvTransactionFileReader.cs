
using Finance.Application.Abstractions;
using Finance.Domain.Entities;

namespace Finance.Infrastructure.Import;

public class CsvTransactionFileReader : ITransactionFileReader
{
    public List<Transaction> ReadExpenses(bool includeRent) => ReaderHelper.ReadRecords(includeRent);

    public List<Transaction> ReadIncome() => ReaderHelper.ReadIncome();
}

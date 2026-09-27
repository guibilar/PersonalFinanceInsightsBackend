
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

/// <summary>Reads bank exports into (unsaved) transactions for the file sync.</summary>
public interface ITransactionFileReader
{
    List<Transaction> ReadExpenses(bool includeRent);
    List<Transaction> ReadIncome();
}

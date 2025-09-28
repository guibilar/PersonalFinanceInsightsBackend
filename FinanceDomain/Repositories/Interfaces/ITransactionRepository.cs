using FinanceDomain.Models;
using FinanceDto.Inputs;

namespace FinanceDomain.Repositories.Interfaces;

public interface ITransactionRepository
{
    Transaction AddAsync(Transaction transaction);
    void AddList(List<Transaction> transactions);
    public List<Transaction> GetAll();
    public List<Transaction> GetForCategoryReport();
    public List<Transaction> Filter(TransactionFilterDto? filter);
}
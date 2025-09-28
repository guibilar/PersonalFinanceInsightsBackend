using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Enums;
using FinanceDto.Inputs;

namespace FinanceDomain.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private List<Transaction> storedTransactions = [];

    public Transaction AddAsync(Transaction transaction)
    {
        transaction.Id = Guid.NewGuid();
        storedTransactions.Add(transaction);
        return transaction;
    }

    public void AddList(List<Transaction> transactions)
    {
        foreach (var transaction in transactions)
        {
            transaction.Id = Guid.NewGuid();
            storedTransactions.Add(transaction);
        }
    }

    public Transaction? GetById(Guid id)
    {
        return storedTransactions.FirstOrDefault(t => t.Id == id);
    }

    public List<Transaction> GetAll()
    {
        return storedTransactions.OrderByDescending(x => x.Date).ToList();
    }

    public List<Transaction> GetForCategoryReport()
    {
        return storedTransactions
            .Where(x => x.Account != AccountType.Savings && x.TransactionType == TransactionType.Debit)
            .OrderByDescending(x => x.Date).ToList();
    }

    public List<Transaction> Filter(TransactionFilterDto? filter)
    {
        var query = storedTransactions.AsQueryable();

        if (filter == null) return query.ToList();

        if (filter.TransactionId.HasValue)
        {
            query = query.Where(x => x.Id == filter.TransactionId.Value);
        }

        if (filter.TransactionType.HasValue)
        {
            query = query.Where(x => x.TransactionType == filter.TransactionType.Value);
        }

        if (filter.AccountType.HasValue)
        {
            query = query.Where(x => x.Account == filter.AccountType.Value);
        }

        if (filter.StartDate.HasValue)
        {
            query = query.Where(x => x.Date >= filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(x => x.Date <= filter.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(filter.Description))
        {
            query = query.Where(x =>
                x.Description.Contains(filter.Description, StringComparison.InvariantCultureIgnoreCase));
        }

        if (!string.IsNullOrEmpty(filter.Category))
        {
            query = query.Where(x => x.Category.Contains(filter.Category, StringComparison.InvariantCultureIgnoreCase));
        }

        return query.ToList();
    }

    public void Remove(Guid id)
    {
        storedTransactions.RemoveAll(t => t.Id == id);
    }
}
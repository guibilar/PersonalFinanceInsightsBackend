using CrossCutting.Helpers;
using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Inputs;
using Microsoft.Extensions.Logging;

namespace CrossCutting.Services;

using CrossCutting.Calculators;
using FinanceDto.Models;

public class TransactionService(ILogger<TransactionService> logger, ITransactionRepository transactionRepository) : ITransactionService
{
    ILogger<TransactionService> _logger = logger;

    public Transaction Add(Transaction transactionInputDto)
    {
        return transactionRepository.AddAsync(transactionInputDto);
    }

    public void SyncTransactionsByFile()
    {
        var records = ReaderHelper.ReadRecords(includeRent: true);
        transactionRepository.AddList(records);
        var recordsIncome = ReaderHelper.ReadIncome();
        transactionRepository.AddList(recordsIncome);
    }

    public List<Transaction> GetAll()
    {
        return transactionRepository.GetAll();
    }
    

    public List<Transaction> Filter(TransactionFilterDto? filter)
    {
        return transactionRepository.Filter(filter);
    }

}

public interface ITransactionService
{
    Transaction Add(Transaction transactionInputDto);
    void SyncTransactionsByFile();
    List<Transaction> GetAll();
    List<Transaction> Filter(TransactionFilterDto? filter);
}
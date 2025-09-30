using CrossCutting.Helpers;
using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Inputs;
using Microsoft.Extensions.Logging;

namespace CrossCutting.Services;

public class TransactionService(ILogger<TransactionService> logger, ITransactionRepository transactionRepository) : ITransactionService
{
    ILogger<TransactionService> _logger = logger;

    public async Task<Transaction> AddAsync(Transaction transactionInputDto)
    {
        return await transactionRepository.AddAsync(transactionInputDto);
    }

    public async Task SyncTransactionsByFileAsync()
    {
        var records = ReaderHelper.ReadRecords(includeRent: true);
        await transactionRepository.AddListAsync(records);
        var recordsIncome = ReaderHelper.ReadIncome();
        await transactionRepository.AddListAsync(recordsIncome);
    }

    public async Task<List<Transaction>> GetAllAsync()
    {
        return await transactionRepository.GetAllAsync();
    }
    

    public async Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter)
    {
        return await transactionRepository.FilterAsync(filter);
    }

}

public interface ITransactionService
{
    Task<Transaction> AddAsync(Transaction transactionInputDto);
    Task SyncTransactionsByFileAsync();
    Task<List<Transaction>> GetAllAsync();
    Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter);
}
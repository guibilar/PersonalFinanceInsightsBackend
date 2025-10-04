using CrossCutting.Helpers;
using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Inputs;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using CrossCutting.Services.Interfaces;
using FinanceDto.Helper;

namespace CrossCutting.Services;

public class TransactionService(ILogger<TransactionService> logger, ITransactionRepository transactionRepository) : ITransactionService
{
    public async Task<Transaction> AddAsync(Transaction transaction)
    {
        var existingTransaction = await transactionRepository.GetByHashAsync(transaction.Hash);
        if (existingTransaction == null) return await transactionRepository.AddAsync(transaction);
        logger.LogWarning("Transaction with hash {Hash} already exists", transaction.Hash);
        throw new InvalidOperationException($"Transaction already exists with hash: {transaction.Hash}");

    }

    public async Task SyncTransactionsByFileAsync()
    {
        var records = ReaderHelper.ReadRecords(includeRent: true);
        GenerateTransactionHashForRecord(records);
        await AddTransactionsIfNotExists(records);
        
        var recordsIncome = ReaderHelper.ReadIncome();
        GenerateTransactionHashForRecord(recordsIncome);
        await AddTransactionsIfNotExists(recordsIncome);
    }

    private async Task AddTransactionsIfNotExists(List<Transaction> records)
    {
        foreach (var record in records)
        {
            var existingTransaction = await transactionRepository.GetByHashAsync(record.Hash);
            if (existingTransaction == null) await transactionRepository.AddAsync(record);
        }
    }

    private static void GenerateTransactionHashForRecord(List<Transaction> recordsIncome)
    {
        foreach (var record in recordsIncome)
        {
            var hash = TransactionHashHelper.GenerateTransactionHash(record.Amount, record.Date, record.Description);
            record.Hash = hash;
        }
    }

    public async Task<List<Transaction>> GetAllAsync()
    {
        return await transactionRepository.GetAllAsync();
    }
    

    public async Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter)
    {
        return await transactionRepository.FilterAsync(filter);
    }
    public async Task<Transaction?> GetByHashAsync(string hash)
    {
        return await transactionRepository.GetByHashAsync(hash);
    }

    public async Task RemoveAsync(Guid id)
    {
        await transactionRepository.RemoveAsync(id);
    }

}
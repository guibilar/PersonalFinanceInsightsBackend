using CrossCutting.Helpers;
using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Inputs;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace CrossCutting.Services;

public class TransactionService(ILogger<TransactionService> logger, ITransactionRepository transactionRepository) : ITransactionService
{
    public async Task<Transaction> AddAsync(Transaction transaction)
    {
        var hash = GenerateTransactionHash(transaction.Amount, transaction.Date, transaction.Description);
        transaction.Hash = hash;

        var existingTransaction = await transactionRepository.GetByHashAsync(hash);
        if (existingTransaction == null) return await transactionRepository.AddAsync(transaction);
        logger.LogWarning("Transaction with hash {Hash} already exists", hash);
        throw new InvalidOperationException($"Transaction already exists with hash: {hash}");

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
            var hash = GenerateTransactionHash(record.Amount, record.Date, record.Description);
            var existingTransaction = await transactionRepository.GetByHashAsync(hash);
            if (existingTransaction == null) await transactionRepository.AddAsync(record);
        }
    }

    private void GenerateTransactionHashForRecord(List<Transaction> recordsIncome)
    {
        foreach (var record in recordsIncome)
        {
            var hash = GenerateTransactionHash(record.Amount, record.Date, record.Description);
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

    public string GenerateTransactionHash(decimal amount, DateTime date, string description)
    {
        var input = $"{amount:F2}{date:yyyy-MM-dd}{description?.Trim().ToLowerInvariant()}";
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
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

public interface ITransactionService
{
    Task<Transaction> AddAsync(Transaction transaction);
    Task SyncTransactionsByFileAsync();
    Task<List<Transaction>> GetAllAsync();
    Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter);
    string GenerateTransactionHash(decimal amount, DateTime date, string description);
    Task<Transaction?> GetByHashAsync(string hash);
    Task RemoveAsync(Guid id);
}
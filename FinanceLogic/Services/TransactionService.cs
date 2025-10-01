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
    private readonly ILogger<TransactionService> _logger = logger;

    public async Task<Transaction> AddAsync(Transaction transaction)
    {
        var hash = GenerateTransactionHash(transaction.Amount, transaction.Date, transaction.Description);
        transaction.Hash = hash;

        var existingTransaction = await transactionRepository.GetByHashAsync(hash);
        if (existingTransaction == null) return await transactionRepository.AddAsync(transaction);
        _logger.LogWarning("Transaction with hash {Hash} already exists", hash);
        throw new InvalidOperationException($"Transaction already exists with hash: {hash}");

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

}

public interface ITransactionService
{
    Task<Transaction> AddAsync(Transaction transaction);
    Task SyncTransactionsByFileAsync();
    Task<List<Transaction>> GetAllAsync();
    Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter);
    string GenerateTransactionHash(decimal amount, DateTime date, string description);
    Task<Transaction?> GetByHashAsync(string hash);
}
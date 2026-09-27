using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using Finance.Application.Abstractions;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Helpers;

namespace Finance.Application.Services;

public class TransactionService(
    ILogger<TransactionService> logger,
    ITransactionRepository transactionRepository,
    IBudgetRepository budgetRepository,
    ITransactionFileReader fileReader) : ITransactionService
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
        var records = fileReader.ReadExpenses(includeRent: true);
        GenerateTransactionHashForRecord(records);
        await AddTransactionsIfNotExists(records);

        var recordsIncome = fileReader.ReadIncome();
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


    public async Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto? filter)
    {
        return await transactionRepository.FilterAsync(filter);
    }

    public async Task<Transaction?> GetByHashAsync(string hash)
    {
        return await transactionRepository.GetByHashAsync(hash);
    }

    public async Task RemoveAsync(Guid id)
    {
        _ = await transactionRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Transaction with id {id} not found.");

        await transactionRepository.RemoveAsync(id);
    }

    public async Task LinkTransactionToBudget(Guid budgetId, Guid transactionId)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId);
        
        if (transaction == null)
            throw new KeyNotFoundException($"Transaction with id {transactionId} not found.");

        if (transaction.BudgetId.HasValue)
            throw new InvalidOperationException($"Transaction with id {transactionId} is already linked to a budget.");

        var budget = await budgetRepository.GetByIdAsync(budgetId);

        if (budget == null)
            throw new KeyNotFoundException($"Budget with id {budgetId} not found.");
        
        transaction.BudgetId = budget.Id;
        await transactionRepository.UpdateAsync(transaction);
    }
    
    public async Task UnlinkTransactionFromBudget(Guid transactionId)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId);
        
        if (transaction == null)
            throw new KeyNotFoundException($"Transaction with id {transactionId} not found.");

        transaction.BudgetId = null;
        await transactionRepository.UpdateAsync(transaction);
    }
}
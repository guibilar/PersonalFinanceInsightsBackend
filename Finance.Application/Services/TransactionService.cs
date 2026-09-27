using Finance.Application.Abstractions;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Exceptions;
using Finance.Domain.Helpers;
using Microsoft.Extensions.Logging;

namespace Finance.Application.Services;

public class TransactionService(
    ILogger<TransactionService> logger,
    ITransactionRepository transactionRepository,
    IBudgetRepository budgetRepository,
    ITransactionFileReader fileReader) : ITransactionService
{
    public async Task<Transaction> AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        if (await transactionRepository.ExistsByHashAsync(transaction.Hash, cancellationToken))
        {
            logger.LogWarning("Transaction with hash {Hash} already exists", transaction.Hash);
            throw new ConflictException("An identical transaction (same date, amount and description) already exists.");
        }

        return await transactionRepository.AddAsync(transaction, cancellationToken);
    }

    public async Task SyncTransactionsByFileAsync(CancellationToken cancellationToken = default)
    {
        var records = fileReader.ReadExpenses(includeRent: true);
        records.AddRange(fileReader.ReadIncome());

        var added = 0;
        foreach (var record in records)
        {
            record.Hash = TransactionHashHelper.GenerateTransactionHash(record.Amount, record.Date, record.Description);
            if (await transactionRepository.ExistsByHashAsync(record.Hash, cancellationToken)) continue;

            await transactionRepository.AddAsync(record, cancellationToken);
            added++;
        }

        logger.LogInformation("File sync read {Read} transactions and added {Added}", records.Count, added);
    }

    public Task<List<Transaction>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return transactionRepository.GetAllAsync(cancellationToken);
    }

    public Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default)
    {
        return transactionRepository.FilterAsync(filter, cancellationToken);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(id, cancellationToken)
                          ?? throw new NotFoundException(nameof(Transaction), id);

        await transactionRepository.RemoveAsync(transaction, cancellationToken);
    }

    public async Task LinkTransactionToBudgetAsync(Guid budgetId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken)
                          ?? throw new NotFoundException(nameof(Transaction), transactionId);

        if (transaction.BudgetId.HasValue)
            throw new ConflictException($"Transaction with id {transactionId} is already linked to a budget.");

        _ = await budgetRepository.GetByIdAsync(budgetId, cancellationToken)
            ?? throw new NotFoundException(nameof(Budget), budgetId);

        transaction.BudgetId = budgetId;
        await transactionRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlinkTransactionFromBudgetAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken)
                          ?? throw new NotFoundException(nameof(Transaction), transactionId);

        transaction.BudgetId = null;
        await transactionRepository.SaveChangesAsync(cancellationToken);
    }
}

using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Finance.Domain.Helpers;
using Microsoft.Extensions.Logging;

namespace Finance.Application.Services;

public class TransactionService(
    ILogger<TransactionService> logger,
    ITransactionRepository transactionRepository,
    IBudgetRepository budgetRepository,
    IStatementParser statementParser) : ITransactionService
{
    private const string DuplicateMessage = "An identical transaction (same account, date, amount and description) already exists.";

    public async Task<Transaction> AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        if (await transactionRepository.ExistsByHashAsync(transaction.Account, transaction.Hash, cancellationToken))
        {
            logger.LogWarning("Transaction with hash {Hash} already exists on {Account}", transaction.Hash, transaction.Account);
            throw new ConflictException(DuplicateMessage);
        }

        try
        {
            return await transactionRepository.AddAsync(transaction, cancellationToken);
        }
        catch (DuplicateEntityException)
        {
            throw new ConflictException(DuplicateMessage);
        }
    }

    public async Task<ImportResult> ImportAsync(Stream statement, AccountType account, CancellationToken cancellationToken = default)
    {
        var parsed = statementParser.Parse(statement, account);

        // Number identical rows (same day, amount and description) so repeated purchases get distinct
        // hashes, while re-importing the same statement produces the same hashes and adds nothing.
        var occurrences = new Dictionary<string, int>();
        foreach (var transaction in parsed)
        {
            var key = $"{transaction.Date:yyyy-MM-dd}|{transaction.Amount:F2}|{transaction.Description.Trim().ToLowerInvariant()}";
            var occurrence = occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
            transaction.Hash = TransactionHashHelper.GenerateTransactionHash(
                transaction.Amount, transaction.Date, transaction.Description, occurrence);
        }

        // The statement is imported completely or not at all.
        var added = await transactionRepository.AddMissingAsync(account, parsed, cancellationToken);

        logger.LogInformation("Imported {Account} statement: {Read} read, {Added} added", account, parsed.Count, added);
        return new ImportResult(parsed.Count, added, parsed.Count - added);
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

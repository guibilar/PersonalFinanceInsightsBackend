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
    ICategoryRuleRepository categoryRuleRepository,
    IStatementParser statementParser) : ITransactionService
{
    private const string DuplicateMessage = "An identical transaction (same account, date, amount and description) already exists.";

    public async Task<Transaction> AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        var existing = await transactionRepository.FindByHashAsync(transaction.Account, transaction.Hash, cancellationToken);
        if (existing is not null)
        {
            logger.LogWarning("Transaction with hash {Hash} already exists on {Account}", transaction.Hash, transaction.Account);
            throw new ConflictException(existing.Hidden
                ? "An identical transaction was deleted earlier; restore it from the deleted transactions instead."
                : DuplicateMessage);
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
        var parsed = statementParser.Parse(statement, account, await GetCategoryMatcherAsync(cancellationToken));

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

        // Where a chequing statement brings a month's real rent payment, the rent generated there before goes away,
        // so the month counts rent once. Hidden, not deleted: it can be restored. (Other accounts never had it; a
        // rent-category purchase on the card, say a storage unit, is not the apartment's rent.)
        var rentMonths = parsed
            .Where(t => t is { Account: AccountType.Chequing, Category: Category.Rent, TransactionType: TransactionType.Debit })
            .Select(t => new DateTime(t.Date.Year, t.Date.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .Distinct()
            .ToList();
        if (rentMonths.Count > 0)
        {
            var replaced = await transactionRepository.HideLegacyMonthlyRentAsync(rentMonths, cancellationToken);
            if (replaced > 0) logger.LogInformation("Hid {Count} generated monthly rent rows replaced by real payments", replaced);
        }

        logger.LogInformation("Imported {Account} statement: {Read} read, {Added} added", account, parsed.Count, added);
        return new ImportResult(parsed.Count, added, parsed.Count - added);
    }

    public Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default)
    {
        return transactionRepository.FilterAsync(filter, cancellationToken);
    }

    // Never removed for good: the stored hash is what keeps a re-imported statement from adding it back.
    public Task HideAsync(Guid id, CancellationToken cancellationToken = default) =>
        SetHiddenAsync(id, hidden: true, cancellationToken);

    public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default) =>
        SetHiddenAsync(id, hidden: false, cancellationToken);

    // Idempotent: hiding a hidden transaction (or restoring a visible one) just succeeds.
    private async Task SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken)
    {
        var transaction = await transactionRepository.GetIncludingHiddenAsync(id, cancellationToken)
                          ?? throw new NotFoundException(nameof(Transaction), id);

        transaction.Hidden = hidden;
        await transactionRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<Transaction> UpdateAsync(Guid id, string description, Category category, CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(id, cancellationToken)
                          ?? throw new NotFoundException(nameof(Transaction), id);

        // The hash is left as imported: it identifies the statement row, so a re-import still recognises it.
        transaction.Description = description.Trim();
        // Only a category the user actually changed is locked; fixing just the description leaves it to the rules.
        if (transaction.Category != category)
        {
            transaction.Category = category;
            transaction.CategoryLocked = true;
        }
        await transactionRepository.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task<CategoryRulesResult> ApplyCategoryRulesAsync(CancellationToken cancellationToken = default)
    {
        var categories = await GetCategoryMatcherAsync(cancellationToken);
        var transactions = await transactionRepository.GetRuleCategorizedAsync(cancellationToken);

        var updated = 0;
        foreach (var transaction in transactions)
        {
            var category = categories.Categorize(transaction.Description);
            if (category == transaction.Category) continue;
            transaction.Category = category;
            updated++;
        }

        await transactionRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Applied category rules: {Checked} checked, {Updated} updated", transactions.Count, updated);
        return new CategoryRulesResult(transactions.Count, updated);
    }

    private async Task<CategoryMatcher> GetCategoryMatcherAsync(CancellationToken cancellationToken) =>
        new(await categoryRuleRepository.GetAllAsync(cancellationToken));

    public async Task LinkTransactionToBudgetAsync(Guid budgetId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken)
                          ?? throw new NotFoundException(nameof(Transaction), transactionId);

        if (transaction.BudgetId.HasValue)
            throw new ConflictException($"Transaction with id {transactionId} is already linked to a budget.");

        var budget = await budgetRepository.GetByIdAsync(budgetId, cancellationToken)
                     ?? throw new NotFoundException(nameof(Budget), budgetId);

        // Inactive budgets are closed: they keep their transactions but take no new ones.
        if (budget.Status != Status.Active)
            throw new ConflictException($"Budget '{budget.Name}' is inactive; activate it before linking transactions.");

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

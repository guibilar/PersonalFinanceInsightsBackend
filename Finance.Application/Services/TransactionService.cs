using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Application.Models;
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
    IAccountRepository accountRepository,
    ICategoryRepository categoryRepository,
    ITagRepository tagRepository,
    TransactionRules rules) : ITransactionService
{
    private const string DuplicateMessage = "An identical transaction (same account, date, amount and description) already exists.";

    public async Task<Transaction> AddAsync(Transaction transaction, bool allowDuplicate = false,
        CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.GetByIdAsync(transaction.AccountId, cancellationToken)
                      ?? throw new NotFoundException(nameof(Account), transaction.AccountId);
        if (transaction.FlowLocked && !FlowMatcher.IsAllowed(transaction.Flow, transaction.TransactionType))
        {
            throw new InvalidInputException("flow", transaction.TransactionType == TransactionType.Debit
                ? "Money out can be spending, a transfer or ignored."
                : "Money in can be income, a refund, a transfer or ignored.");
        }
        if (transaction.CategoryLocked)
        {
            await GetCategoryAsync(transaction.CategoryId, cancellationToken);
        }
        // What wasn't picked follows the rules, like an imported row's.
        (await rules.ClassifierAsync(cancellationToken)).Classify(transaction, account.Type);
        // For the response; the rules only ever give a category that exists.
        transaction.Category = await categoryRepository.GetByIdAsync(transaction.CategoryId, cancellationToken);

        var existing = await transactionRepository.FindByHashAsync(transaction.AccountId, transaction.Hash, cancellationToken);
        if (existing is not null && !allowDuplicate)
        {
            logger.LogWarning("Transaction with hash {Hash} already exists on {Account}", transaction.Hash, account.Name);
            throw new ConflictException(existing.Hidden
                ? "An identical transaction was deleted earlier; restore it from the deleted transactions instead."
                : DuplicateMessage);
        }
        if (existing is not null)
        {
            // Numbered like identical rows of a statement: the first free number.
            for (var occurrence = 2; existing is not null; occurrence++)
            {
                transaction.Hash = TransactionHashHelper.GenerateTransactionHash(
                    transaction.Amount, transaction.Date, transaction.Description, occurrence);
                existing = await transactionRepository.FindByHashAsync(transaction.AccountId, transaction.Hash, cancellationToken);
            }
        }

        try
        {
            await transactionRepository.AddAsync(transaction, cancellationToken);
            transaction.Account = account;
            return transaction;
        }
        catch (DuplicateEntityException)
        {
            throw new ConflictException(DuplicateMessage);
        }
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

    public async Task<Transaction> UpdateAsync(Guid id, string description, Guid categoryId, Flow? flow, string? note = null,
        IReadOnlyCollection<Guid>? tagIds = null, CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(id, cancellationToken)
                          ?? throw new NotFoundException(nameof(Transaction), id);
        if (flow is { } picked && picked != transaction.Flow)
        {
            if (!FlowMatcher.IsAllowed(picked, transaction.TransactionType))
            {
                throw new InvalidInputException("flow", transaction.TransactionType == TransactionType.Debit
                    ? "Money out can be spending, a transfer or ignored."
                    : "Money in can be income, a refund, a transfer or ignored.");
            }
            transaction.Flow = picked;
            transaction.FlowLocked = true;
        }

        // The hash is left as imported: it identifies the statement row, so a re-import still recognises it.
        transaction.Description = description.Trim();
        // Only a category the user actually changed is locked; fixing just the description leaves it to the rules.
        if (transaction.CategoryId != categoryId)
        {
            transaction.Category = await GetCategoryAsync(categoryId, cancellationToken);
            transaction.CategoryId = categoryId;
            transaction.CategoryLocked = true;
        }
        if (note is not null) transaction.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (tagIds is not null) transaction.Tags = await GetTagsAsync(tagIds, cancellationToken);
        await transactionRepository.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task<RulesResult> ApplyRulesAsync(CancellationToken cancellationToken = default)
    {
        var classifier = await rules.ClassifierAsync(cancellationToken);
        var transactions = await transactionRepository.GetRuleManagedAsync(cancellationToken);
        // Every one is classified, deleted ones included; the counts are of those the list shows.
        var updated = transactions.Count(t => classifier.Classify(t, t.Account!.Type) && !t.Hidden);
        var visible = transactions.Count(t => !t.Hidden);

        await transactionRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Applied the rules: {Checked} checked, {Updated} updated", visible, updated);
        return new RulesResult(visible, updated);
    }

    private async Task<List<Tag>> GetTagsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        var tags = await tagRepository.GetByIdsAsync(distinct, cancellationToken);
        if (tags.Count != distinct.Count) throw new InvalidInputException("tagIds", "A tag doesn't exist (deleted meanwhile?).");
        return tags;
    }

    private async Task<Category> GetCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        await categoryRepository.GetByIdAsync(id, cancellationToken)
        ?? throw new InvalidInputException("categoryId", "That category doesn't exist.");

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

    /// <summary>
    /// Sets a category, a flow, a budget or a tag on many transactions at once, with the same rules as one by one: a category or
    /// flow set this way is kept from the rules, a flow only where it fits the direction, and only active budgets.
    /// </summary>
    public async Task<BulkUpdateResult> BulkUpdateAsync(BulkUpdate update, CancellationToken cancellationToken = default)
    {
        var category = update.CategoryId is { } categoryId ? await GetCategoryAsync(categoryId, cancellationToken) : null;
        Budget? budget = null;
        if (update.BudgetId is { } budgetId)
        {
            budget = await budgetRepository.GetByIdAsync(budgetId, cancellationToken)
                     ?? throw new InvalidInputException("budgetId", "That budget doesn't exist.");
            if (budget.Status != Status.Active)
                throw new ConflictException($"Budget '{budget.Name}' is inactive; activate it before linking transactions.");
        }

        var addTag = update.AddTagId is { } addId ? (await GetTagsAsync([addId], cancellationToken))[0] : null;
        var removeTag = update.RemoveTagId;

        var ids = update.TransactionIds.Distinct().ToList();
        var transactions = await transactionRepository.GetByIdsAsync(ids, cancellationToken);
        var updated = 0;
        var refusedFlow = 0;
        foreach (var transaction in transactions)
        {
            var changed = false;
            if (category is not null && transaction.CategoryId != category.Id)
            {
                transaction.CategoryId = category.Id;
                transaction.Category = category;
                transaction.CategoryLocked = true;
                changed = true;
            }
            if (update.Flow is { } flow && flow != transaction.Flow)
            {
                if (FlowMatcher.IsAllowed(flow, transaction.TransactionType))
                {
                    transaction.Flow = flow;
                    transaction.FlowLocked = true;
                    changed = true;
                }
                else
                {
                    refusedFlow++;
                }
            }
            if (budget is not null && transaction.BudgetId != budget.Id)
            {
                transaction.BudgetId = budget.Id;
                changed = true;
            }
            if (update.UnlinkBudget && transaction.BudgetId is not null)
            {
                transaction.BudgetId = null;
                changed = true;
            }
            if (addTag is not null && transaction.Tags.All(t => t.Id != addTag.Id))
            {
                transaction.Tags.Add(addTag);
                changed = true;
            }
            if (removeTag is not null && transaction.Tags.RemoveAll(t => t.Id == removeTag) > 0)
            {
                changed = true;
            }
            if (changed) updated++;
        }
        await transactionRepository.SaveChangesAsync(cancellationToken);

        var missing = ids.Count - transactions.Count;
        var notes = new List<string>();
        if (refusedFlow > 0)
        {
            notes.Add($"{refusedFlow} kept {(refusedFlow == 1 ? "its" : "their")} flow: money out can be spending, a transfer or ignored, " +
                      "money in income, a refund, a transfer or ignored.");
        }
        if (missing > 0)
        {
            notes.Add($"{missing} {(missing == 1 ? "wasn't" : "weren't")} found: deleted meanwhile, perhaps.");
        }
        logger.LogInformation("Bulk update: {Updated} of {Count} transactions changed", updated, ids.Count);
        return new BulkUpdateResult(updated, refusedFlow + missing, notes);
    }

    public async Task UnlinkTransactionFromBudgetAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await transactionRepository.GetByIdAsync(transactionId, cancellationToken)
                          ?? throw new NotFoundException(nameof(Transaction), transactionId);

        transaction.BudgetId = null;
        await transactionRepository.SaveChangesAsync(cancellationToken);
    }
}

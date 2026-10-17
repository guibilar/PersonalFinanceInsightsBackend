using Finance.Application.Abstractions;
using Finance.Application.Events;
using Finance.Application.Import;
using Finance.Application.Models;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Finance.Domain.Helpers;
using Hestia.Contracts.Finance;
using Microsoft.Extensions.Logging;

namespace Finance.Application.Services;

public class TransactionService(
    ILogger<TransactionService> logger,
    ITransactionRepository transactionRepository,
    IBudgetRepository budgetRepository,
    IAccountRepository accountRepository,
    ICategoryRepository categoryRepository,
    ITagRepository tagRepository,
    TransactionRules rules,
    ReportingCalendar calendar,
    FinanceChanges changes) : ITransactionService
{
    private const string DuplicateMessage = "An identical transaction (same account, date, amount and description) already exists.";

    /// <summary>The first year a transaction can be in, as for the reviews.</summary>
    private const int FirstYear = 2000;

    // Each change below runs in FinanceChanges (T04): with the finance events on, the change and its events commit
    // together, and a change that moves a budget past 80 % or 100 % also sends BudgetLimitReached.

    public Task<Transaction> AddAsync(Transaction transaction, bool allowDuplicate = false,
        CancellationToken cancellationToken = default) =>
        changes.RunAsync(ChangeEffect.Spending, ct => AddOnceAsync(transaction, allowDuplicate, ct), cancellationToken);

    private async Task<Transaction> AddOnceAsync(Transaction transaction, bool allowDuplicate, CancellationToken cancellationToken)
    {
        var account = await accountRepository.GetByIdAsync(transaction.AccountId, cancellationToken)
                      ?? throw new NotFoundException(nameof(Account), transaction.AccountId);
        // A mistyped year ("0206", "9999") would stretch every report from the first transaction to the last: the
        // breakdown then has too many periods to show, or none past the calendar's end.
        var latest = calendar.Today().AddYears(1);
        if (transaction.Date.Year < FirstYear || transaction.Date > latest)
        {
            throw new InvalidInputException("date", $"Choose a date from {FirstYear} to {latest:yyyy-MM-dd}.");
        }
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
            // This attempt's account: one after a database failure starts with nothing tracked (IUnitOfWork).
            transaction.Account = account;
            await transactionRepository.AddAsync(transaction, cancellationToken);
        }
        catch (DuplicateEntityException)
        {
            throw new ConflictException(DuplicateMessage);
        }
        await changes.RaiseChangedAsync([transaction], TransactionChange.Added, cancellationToken);
        await changes.RaisePriceChangesAsync([transaction], cancellationToken);
        return transaction;
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
    private Task SetHiddenAsync(Guid id, bool hidden, CancellationToken cancellationToken) =>
        changes.RunAsync(ChangeEffect.Spending, async ct =>
        {
            var transaction = await transactionRepository.GetIncludingHiddenAsync(id, ct)
                              ?? throw new NotFoundException(nameof(Transaction), id);

            var changed = transaction.Hidden != hidden;
            transaction.Hidden = hidden;
            if (changed)
            {
                await changes.RaiseChangedAsync([transaction], hidden ? TransactionChange.Deleted : TransactionChange.Restored, ct);
            }
            await transactionRepository.SaveChangesAsync(ct);
        }, cancellationToken);

    public Task<Transaction> UpdateAsync(Guid id, string description, Guid categoryId, Flow? flow, string? note = null,
        IReadOnlyCollection<Guid>? tagIds = null, CancellationToken cancellationToken = default) =>
        changes.RunAsync(ChangeEffect.Spending,
            ct => UpdateOnceAsync(id, description, categoryId, flow, note, tagIds, ct), cancellationToken);

    private async Task<Transaction> UpdateOnceAsync(Guid id, string description, Guid categoryId, Flow? flow, string? note,
        IReadOnlyCollection<Guid>? tagIds, CancellationToken cancellationToken)
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
        await changes.RaiseChangedAsync([transaction], TransactionChange.Updated, cancellationToken);
        await transactionRepository.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public Task<RulesResult> ApplyRulesAsync(CancellationToken cancellationToken = default) =>
        changes.RunAsync(ChangeEffect.Spending, async ct =>
        {
            var classifier = await rules.ClassifierAsync(ct);
            var transactions = await transactionRepository.GetRuleManagedAsync(ct);
            // Every one is classified, deleted ones included; the counts (and the events) are of those the list shows.
            var updated = transactions.Where(t => classifier.Classify(t, t.Account!.Type) && !t.Hidden).ToList();
            var visible = transactions.Count(t => !t.Hidden);

            await changes.RaiseChangedAsync(updated, TransactionChange.Updated, ct);
            await transactionRepository.SaveChangesAsync(ct);
            logger.LogInformation("Applied the rules: {Checked} checked, {Updated} updated", visible, updated.Count);
            return new RulesResult(visible, updated.Count);
        }, cancellationToken);

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

    public Task LinkTransactionToBudgetAsync(Guid budgetId, Guid transactionId, CancellationToken cancellationToken = default) =>
        changes.RunAsync(ChangeEffect.Spending, ct => LinkOnceAsync(budgetId, transactionId, ct), cancellationToken);

    private async Task LinkOnceAsync(Guid budgetId, Guid transactionId, CancellationToken cancellationToken)
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
        await changes.RaiseChangedAsync([transaction], TransactionChange.Updated, cancellationToken);
        await transactionRepository.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sets a category, a flow, a budget or a tag on many transactions at once, with the same rules as one by one: a category or
    /// flow set this way is kept from the rules, a flow only where it fits the direction, and only active budgets.
    /// </summary>
    public Task<BulkUpdateResult> BulkUpdateAsync(BulkUpdate update, CancellationToken cancellationToken = default) =>
        changes.RunAsync(ChangeEffect.Spending, ct => BulkUpdateOnceAsync(update, ct), cancellationToken);

    private async Task<BulkUpdateResult> BulkUpdateOnceAsync(BulkUpdate update, CancellationToken cancellationToken)
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
        var refusedFlow = 0;
        var changedTransactions = new List<Transaction>();
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
            if (changed) changedTransactions.Add(transaction);
        }
        var updated = changedTransactions.Count;
        await changes.RaiseChangedAsync(changedTransactions, TransactionChange.Updated, cancellationToken);
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

    public Task UnlinkTransactionFromBudgetAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        changes.RunAsync(ChangeEffect.Spending, async ct =>
        {
            var transaction = await transactionRepository.GetByIdAsync(transactionId, ct)
                              ?? throw new NotFoundException(nameof(Transaction), transactionId);

            var changed = transaction.BudgetId is not null;
            transaction.BudgetId = null;
            if (changed)
            {
                await changes.RaiseChangedAsync([transaction], TransactionChange.Updated, ct);
            }
            await transactionRepository.SaveChangesAsync(ct);
        }, cancellationToken);
}

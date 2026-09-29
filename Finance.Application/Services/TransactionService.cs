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
    IFlowRuleRepository flowRuleRepository,
    IAccountRepository accountRepository,
    IEnumerable<IStatementParser> statementParsers) : ITransactionService
{
    private const string DuplicateMessage = "An identical transaction (same account, date, amount and description) already exists.";

    // A statement is taken for another account's when at least this many of its rows, and half of them, are stored there.
    private const int MismatchMinimumRows = 3;

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
        // What wasn't picked follows the rules, like an imported row's.
        (await GetClassifierAsync(cancellationToken)).Classify(transaction, account.Type);

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

    public async Task<ImportResult> ImportAsync(Stream statement, Guid accountId, bool confirmed,
        CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.GetByIdAsync(accountId, cancellationToken)
                      ?? throw new NotFoundException(nameof(Account), accountId);
        if (account.Format == StatementFormat.Manual)
        {
            throw new InvalidInputException("accountId",
                $"\"{account.Name}\" has no statement files: add its transactions by hand, or change its statement format.");
        }
        var parser = statementParsers.FirstOrDefault(p => p.Format == account.Format)
                     ?? throw new InvalidInputException("accountId", $"Statements in the {account.Format} format can't be read yet.");
        var statementRows = parser.Parse(statement);
        var parsed = statementRows.Transactions;
        var classifier = await GetClassifierAsync(cancellationToken);
        foreach (var transaction in parsed)
        {
            transaction.AccountId = account.Id;
            classifier.Classify(transaction, account.Type);
        }

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

        if (!confirmed)
        {
            await EnsureStatementMatchesAsync(account, statementRows, cancellationToken);
        }

        // The statement is imported completely or not at all.
        var added = await transactionRepository.AddMissingAsync(account.Id, parsed, cancellationToken);

        // Where a chequing statement brings a month's real rent payment, the rent generated there before goes away,
        // so the month counts rent once. Hidden, not deleted: it can be restored. (Other accounts never had it; a
        // rent-category purchase on the card, say a storage unit, is not the apartment's rent.)
        var rentMonths = parsed
            .Where(t => account.Type == AccountType.Chequing && t is { Category: Category.Rent, TransactionType: TransactionType.Debit })
            .Select(t => new DateTime(t.Date.Year, t.Date.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .Distinct()
            .ToList();
        if (rentMonths.Count > 0)
        {
            var replaced = await transactionRepository.HideLegacyMonthlyRentAsync(rentMonths, cancellationToken);
            if (replaced > 0) logger.LogInformation("Hid {Count} generated monthly rent rows replaced by real payments", replaced);
        }

        logger.LogInformation("Imported {Account} statement: {Read} read, {Added} added", account.Name, parsed.Count, added);
        return new ImportResult(parsed.Count, added, parsed.Count - added);
    }

    /// <summary>
    /// Catches the account picked by mistake: a credit card export imported into a bank account, or a statement most of
    /// whose rows are already stored on another account (imported there before, so this is probably that account's).
    /// </summary>
    private async Task EnsureStatementMatchesAsync(Account account, ParsedStatement statement, CancellationToken cancellationToken)
    {
        if (statement.IsCreditCardExport && account.Type != AccountType.CreditCard)
        {
            throw new AccountMismatchException(
                $"This file is a credit card export, but \"{account.Name}\" is a {Describe(account.Type)} account.");
        }

        var hashes = statement.Transactions.Select(t => t.Hash).ToList();
        var stored = await transactionRepository.CountHashesByAccountAsync(hashes, cancellationToken);
        var here = stored.GetValueOrDefault(account.Id);
        var (otherId, elsewhere) = stored.Where(s => s.Key != account.Id)
            .Select(s => (s.Key, s.Value))
            .OrderByDescending(s => s.Value)
            .FirstOrDefault();
        if (elsewhere >= MismatchMinimumRows && elsewhere * 2 >= hashes.Count && elsewhere > here)
        {
            var other = await accountRepository.GetByIdAsync(otherId, cancellationToken);
            throw new AccountMismatchException(
                $"{elsewhere} of the {hashes.Count} transactions in this file are already in \"{other?.Name}\". " +
                $"Is \"{account.Name}\" the right account?");
        }
    }

    private static string Describe(AccountType type) => type switch
    {
        AccountType.CreditCard => "credit card",
        _ => type.ToString().ToLowerInvariant()
    };

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

    public async Task<Transaction> UpdateAsync(Guid id, string description, Category category, Flow? flow,
        CancellationToken cancellationToken = default)
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
        if (transaction.Category != category)
        {
            transaction.Category = category;
            transaction.CategoryLocked = true;
        }
        await transactionRepository.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task<RulesResult> ApplyRulesAsync(CancellationToken cancellationToken = default)
    {
        var classifier = await GetClassifierAsync(cancellationToken);
        var transactions = await transactionRepository.GetRuleManagedAsync(cancellationToken);
        var updated = transactions.Count(t => classifier.Classify(t, t.Account!.Type));

        await transactionRepository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Applied the rules: {Checked} checked, {Updated} updated", transactions.Count, updated);
        return new RulesResult(transactions.Count, updated);
    }

    private async Task<TransactionClassifier> GetClassifierAsync(CancellationToken cancellationToken) =>
        new(new CategoryMatcher(await categoryRuleRepository.GetAllAsync(cancellationToken)),
            await GetFlowMatcherAsync(cancellationToken));

    private async Task<FlowMatcher> GetFlowMatcherAsync(CancellationToken cancellationToken) =>
        new(await flowRuleRepository.GetAllAsync(cancellationToken));

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

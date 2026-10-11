using Finance.Application.Abstractions;
using Finance.Application.Events;
using Finance.Application.Import;
using Finance.Application.Services.Interfaces;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Finance.Domain.Helpers;
using Hestia.Contracts.Finance;
using Microsoft.Extensions.Logging;

namespace Finance.Application.Services;

/// <summary>
/// Brings a statement file into an account: read with the account's format, classified by the rules, added where not
/// already stored, with the statement's closing balance saved.
/// </summary>
public class StatementImportService(
    ILogger<StatementImportService> logger,
    ITransactionRepository transactionRepository,
    IAccountRepository accountRepository,
    IEnumerable<IStatementParser> statementParsers,
    TransactionRules rules,
    FinanceChanges changes,
    TimeProvider timeProvider) : IStatementImportService
{
    // A statement is taken for another account's when at least this many of its rows, and half of them, are stored there.
    private const int MismatchMinimumRows = 3;

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
        var classifier = await rules.ClassifierAsync(cancellationToken);
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

        // The statement is imported completely or not at all. With the finance events on, the import and its events
        // are one transaction (T04): the rows, the balance, and the events commit together.
        return await changes.RunAsync(ChangeEffect.Spending, async ct =>
        {
            var added = await transactionRepository.AddMissingAsync(account.Id, parsed, ct);

            // Where a chequing statement brings a month's real rent payment, the rent generated there before goes away,
            // so the month counts rent once. Hidden, not deleted: it can be restored. (Other accounts never had it; a
            // rent-category purchase on the card, say a storage unit, is not the apartment's rent.)
            var rentMonths = parsed
                .Where(t => account.Type == AccountType.Chequing && t.CategoryId == Category.RentId && t.TransactionType == TransactionType.Debit)
                .Select(t => new DateTime(t.Date.Year, t.Date.Month, 1, 0, 0, 0, DateTimeKind.Utc))
                .Distinct()
                .ToList();
            if (rentMonths.Count > 0)
            {
                var replaced = await transactionRepository.HideLegacyMonthlyRentAsync(rentMonths, ct);
                if (replaced > 0) logger.LogInformation("Hid {Count} generated monthly rent rows replaced by real payments", replaced);
            }

            // The statement's own figure for what the account was worth replaces any typed in for that day.
            if (statementRows.ClosingBalance is { } closing)
            {
                await accountRepository.SetBalanceAsync(account.Id, closing.Date, closing.Amount, ct);
            }

            // A statement read in full counts as imported even when every row was already there: the reminders to import
            // ask whether statements are being brought in, not whether they had anything new.
            account.LastImportedAt = timeProvider.GetUtcNow().UtcDateTime;
            await RaiseImportedAsync(account, parsed, added, statementRows.ClosingBalance, ct);
            await accountRepository.SaveChangesAsync(ct);

            logger.LogInformation("Imported {Account} statement: {Read} read, {Added} added", account.Name, parsed.Count, added.Count);
            return new ImportResult(parsed.Count, added.Count, parsed.Count - added.Count, statementRows.ClosingBalance);
        }, cancellationToken);
    }

    /// <summary>The events of an import: the import itself, the statement's balance, and new subscription prices.</summary>
    private async Task RaiseImportedAsync(Account account, IReadOnlyList<Transaction> parsed, IReadOnlyList<Transaction> added,
        StatementBalance? closing, CancellationToken cancellationToken)
    {
        if (!changes.Enabled)
        {
            return;
        }
        var now = changes.Now;
        changes.Raise(new TransactionsImported(account.Id, account.Name, parsed.Count, added.Count, parsed.Count - added.Count,
            parsed.Count == 0 ? null : DateOnly.FromDateTime(parsed.Min(t => t.Date)),
            parsed.Count == 0 ? null : DateOnly.FromDateTime(parsed.Max(t => t.Date)),
            now));
        if (closing is not null)
        {
            changes.Raise(new BalanceRecorded(account.Id, account.Name, DateOnly.FromDateTime(closing.Date), closing.Amount,
                BalanceSource.Statement, now));
        }
        await changes.RaisePriceChangesAsync(added, cancellationToken);
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
                $"This file is a credit card export, but \"{account.Name}\" is {Describe(account.Type)} account.");
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

    /// <summary>"a credit card", "an investment".</summary>
    private static string Describe(AccountType type) => type switch
    {
        AccountType.CreditCard => "a credit card",
        AccountType.Investment => "an investment",
        _ => "a " + type.ToString().ToLowerInvariant()
    };
}

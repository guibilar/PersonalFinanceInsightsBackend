using Finance.Application.Abstractions;
using Finance.Application.Events;
using Finance.Application.Models;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Hestia.Contracts.Finance;

namespace Finance.Application.Services;

public class AccountService(IAccountRepository accountRepository, ReportingCalendar calendar, FinanceChanges changes) : IAccountService
{
    /// <summary>The TFSA's first year: no room is older.</summary>
    private const int FirstRoomYear = 2009;

    public Task<List<AccountSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        accountRepository.GetSummariesAsync(calendar.Today().Year, cancellationToken);

    public async Task<AccountSummary> AddAsync(AccountType type, AccountDetails details, CancellationToken cancellationToken = default)
    {
        var account = new Account { Name = details.Name.Trim(), Type = type, Format = details.Format ?? StatementFormat.Cibc };
        EnsureUnique(await accountRepository.GetAllAsync(cancellationToken), account.Name, account.Id);
        Apply(account, details);
        await accountRepository.AddAsync(account, cancellationToken);
        return new AccountSummary(account, 0);
    }

    public async Task<AccountSummary> UpdateAsync(Guid id, AccountDetails details, CancellationToken cancellationToken = default)
    {
        var accounts = await accountRepository.GetAllAsync(cancellationToken);
        var account = accounts.SingleOrDefault(a => a.Id == id) ?? throw new NotFoundException(nameof(Account), id);
        var trimmed = details.Name.Trim();
        EnsureUnique(accounts, trimmed, id);

        account.Name = trimmed;
        if (details.Format is { } newFormat) account.Format = newFormat;
        Apply(account, details);
        await accountRepository.SaveChangesAsync(cancellationToken);
        return await accountRepository.GetSummaryAsync(account, calendar.Today().Year, cancellationToken);
    }

    /// <summary>The plan and its room: only savings and investments are held in one, and only a plan has room.</summary>
    private void Apply(Account account, AccountDetails details)
    {
        if (details.Plan is { } plan)
        {
            if (plan != RegisteredPlan.None && !account.Type.HoldsSavings())
            {
                throw new InvalidInputException("plan", "Only savings and investment accounts can be held in a registered plan.");
            }
            account.Plan = plan;
            if (plan == RegisteredPlan.None) (account.ContributionRoom, account.ContributionRoomYear) = (null, null);
        }
        if (details.ContributionRoom is { } room)
        {
            if (account.Plan == RegisteredPlan.None)
            {
                throw new InvalidInputException("contributionRoom", "Contribution room is for an account held in a registered plan.");
            }
            var thisYear = calendar.Today().Year;
            var year = details.ContributionRoomYear ?? thisYear;
            if (year < FirstRoomYear || year > thisYear)
            {
                throw new InvalidInputException("contributionRoomYear", $"Choose a year from {FirstRoomYear} to {thisYear}.");
            }
            (account.ContributionRoom, account.ContributionRoomYear) = (room, year);
        }
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Account), id);
        // Deleted transactions count too: they are what keeps a re-imported statement from adding them back.
        var transactions = await accountRepository.CountTransactionsAsync(id, cancellationToken);
        if (transactions > 0)
        {
            throw new ConflictException(
                $"\"{account.Name}\" has {transactions} transaction{(transactions == 1 ? "" : "s")} (deleted ones included); only an empty account can be deleted.");
        }
        await accountRepository.RemoveAsync(account, cancellationToken);
    }

    public async Task<List<AccountBalance>> GetBalancesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync(accountId, cancellationToken);
        return await accountRepository.GetBalancesAsync(accountId, cancellationToken);
    }

    public async Task<AccountBalance> SetBalanceAsync(Guid accountId, DateOnly date, decimal amount, CancellationToken cancellationToken = default)
    {
        var account = await EnsureExistsAsync(accountId, cancellationToken);
        var day = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var today = calendar.Today();
        // A phone or browser east of the reporting time zone can already be on tomorrow: that is today here.
        if (day > today.AddDays(1)) throw new InvalidInputException("date", "A balance can't be in the future.");
        if (day > today) day = today;

        // With the finance events on, the balance and its event commit together (T04).
        return await changes.RunAsync(ChangeEffect.Other, async ct =>
        {
            var balance = await accountRepository.SetBalanceAsync(accountId, day, amount, ct);
            changes.Raise(new BalanceRecorded(account.Id, account.Name, DateOnly.FromDateTime(balance.Date), balance.Amount,
                BalanceSource.Entered, changes.Now));
            return balance;
        }, cancellationToken);
    }

    public async Task RemoveBalanceAsync(Guid accountId, Guid balanceId, CancellationToken cancellationToken = default)
    {
        var balance = await accountRepository.GetBalanceByIdAsync(accountId, balanceId, cancellationToken)
                      ?? throw new NotFoundException(nameof(AccountBalance), balanceId);
        await accountRepository.RemoveBalanceAsync(balance, cancellationToken);
    }

    private async Task<Account> EnsureExistsAsync(Guid accountId, CancellationToken cancellationToken) =>
        await accountRepository.GetByIdAsync(accountId, cancellationToken) ?? throw new NotFoundException(nameof(Account), accountId);

    // The database's unique index is the backstop for concurrent requests; this gives the usual case a clear message.
    private static void EnsureUnique(List<Account> accounts, string name, Guid id)
    {
        if (accounts.Any(a => a.Id != id && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException($"An account named \"{name}\" already exists.");
        }
    }
}

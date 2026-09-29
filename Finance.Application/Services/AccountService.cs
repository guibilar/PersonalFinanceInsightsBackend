using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Application.Services.Interfaces;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;

namespace Finance.Application.Services;

public class AccountService(IAccountRepository accountRepository) : IAccountService
{
    public Task<List<AccountSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        accountRepository.GetSummariesAsync(cancellationToken);

    public async Task<AccountSummary> AddAsync(string name, AccountType type, StatementFormat format,
        CancellationToken cancellationToken = default)
    {
        var account = new Account { Name = name.Trim(), Type = type, Format = format };
        EnsureUnique(await accountRepository.GetAllAsync(cancellationToken), account.Name, account.Id);
        await accountRepository.AddAsync(account, cancellationToken);
        return new AccountSummary(account, 0);
    }

    public async Task<AccountSummary> UpdateAsync(Guid id, string name, StatementFormat? format,
        CancellationToken cancellationToken = default)
    {
        var accounts = await accountRepository.GetAllAsync(cancellationToken);
        var account = accounts.SingleOrDefault(a => a.Id == id) ?? throw new NotFoundException(nameof(Account), id);
        var trimmed = name.Trim();
        EnsureUnique(accounts, trimmed, id);

        account.Name = trimmed;
        if (format is { } newFormat) account.Format = newFormat;
        await accountRepository.SaveChangesAsync(cancellationToken);
        return new AccountSummary(account, await accountRepository.CountTransactionsAsync(id, cancellationToken));
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

    // The database's unique index is the backstop for concurrent requests; this gives the usual case a clear message.
    private static void EnsureUnique(List<Account> accounts, string name, Guid id)
    {
        if (accounts.Any(a => a.Id != id && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException($"An account named \"{name}\" already exists.");
        }
    }
}

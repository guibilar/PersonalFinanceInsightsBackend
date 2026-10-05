using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services;

/// <summary>
/// Balances between the ones entered. After a balance, the account's transactions carry it on; before the first one, a
/// bank account's or card's transactions carry it back to just before its first transaction. An investment's value also
/// moves with the market, so it is only known from its first balance, and is moved only by money in and out.
/// </summary>
public class NetWorthService(
    IAccountRepository accountRepository,
    ITransactionRepository transactionRepository,
    ReportingCalendar calendar) : INetWorthService
{
    private const int MonthsShown = 12;

    public async Task<NetWorthReport> GetAsync(CancellationToken cancellationToken = default)
    {
        var today = calendar.Today();
        var accounts = await accountRepository.GetAllAsync(cancellationToken);
        var balances = (await accountRepository.GetAllBalancesAsync(cancellationToken)).ToLookup(b => b.AccountId);
        // Ignored rows (a pending charge listed again once it posts, say) never moved the balance.
        var transactions = (await transactionRepository.FilterNoPaginationAsync(new TransactionFilterDto(), cancellationToken))
            .Where(t => t.TransactionType is not null && t.Flow != Flow.Ignored)
            .ToLookup(t => t.AccountId);

        var histories = accounts
            .Select(a => new History(a, balances[a.Id].OrderBy(b => b.Date).ToList(), transactions[a.Id].OrderBy(t => t.Date).ToList()))
            .ToList();

        var report = new NetWorthReport
        {
            Accounts = histories.Select(h =>
            {
                var latest = h.Balances.LastOrDefault();
                return new NetWorthAccount
                {
                    Id = h.Account.Id,
                    Name = h.Account.Name,
                    Type = h.Account.Type,
                    Plan = h.Account.Plan,
                    Balance = latest is null ? null : h.At(today),
                    BalanceDate = latest?.Date,
                    MovedSince = latest is not null && h.Transactions.Any(t => t.Date > latest.Date && t.Date <= today),
                    IsLiability = h.IsLiability,
                };
            }).ToList(),
        };
        report.Assets = histories.Where(h => !h.IsLiability).Sum(h => h.At(today) ?? 0);
        report.Liabilities = histories.Where(h => h.IsLiability).Sum(h => h.At(today) ?? 0);
        report.NetWorth = report.Assets - report.Liabilities;

        var currentMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = MonthsShown - 1; i >= 0; i--)
        {
            var start = currentMonth.AddMonths(-i);
            var day = i == 0 ? today : start.AddMonths(1).AddDays(-1);
            var known = histories.Select(h => (h.IsLiability, Value: h.At(day))).Where(x => x.Value is not null).ToList();
            if (known.Count == 0) continue;
            var assets = known.Where(x => !x.IsLiability).Sum(x => x.Value!.Value);
            var liabilities = known.Where(x => x.IsLiability).Sum(x => x.Value!.Value);
            report.Months.Add(new NetWorthMonth
            {
                Month = start.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                Date = day,
                Assets = assets,
                Liabilities = liabilities,
                NetWorth = assets - liabilities,
                AccountsCounted = known.Count,
            });
        }
        return report;
    }

    /// <param name="Balances">Oldest first.</param>
    /// <param name="Transactions">Oldest first; only those that move money.</param>
    private sealed record History(Account Account, List<AccountBalance> Balances, List<Transaction> Transactions)
    {
        public bool IsLiability => Account.Type == AccountType.CreditCard;

        /// <summary>How a transaction moves the balance: money in raises it, except on a card, where purchases raise what is owed.</summary>
        private decimal Effect(Transaction transaction)
        {
            // Inside an investment account, buying and selling only move money between cash and holdings.
            if (Account.Type == AccountType.Investment && transaction.Flow != Flow.Transfer) return 0;
            var signed = transaction.TransactionType == TransactionType.Credit ? transaction.Amount : -transaction.Amount;
            return IsLiability ? -signed : signed;
        }

        /// <summary>The balance at the end of <paramref name="day"/>; null when nothing says what it was.</summary>
        public decimal? At(DateTime day)
        {
            if (Balances.Count == 0) return null;
            var before = Balances.LastOrDefault(b => b.Date <= day);
            if (before is not null)
            {
                return before.Amount + Transactions.Where(t => t.Date > before.Date && t.Date <= day).Sum(Effect);
            }
            if (Account.Type == AccountType.Investment) return null;
            // Before the first balance: back through the transactions, but not past the first one imported.
            var first = Balances[0];
            if (Transactions.Count == 0 || day < Transactions[0].Date.AddDays(-1)) return null;
            return first.Amount - Transactions.Where(t => t.Date > day && t.Date <= first.Date).Sum(Effect);
        }
    }
}

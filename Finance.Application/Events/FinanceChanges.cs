using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Application.Reports;
using Finance.Application.Services;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Hestia.Contracts.Finance;

namespace Finance.Application.Events;

/// <summary>What a change can do to the reports.</summary>
public enum ChangeEffect
{
    /// <summary>No budget total changes (a balance, for example).</summary>
    Other,

    /// <summary>Budget totals can change: the change can send <see cref="BudgetLimitReached"/>.</summary>
    Spending
}

/// <summary>
/// Runs a change that publishes finance events (T04, D7). With the events on, the change and its events are one
/// database transaction: the outbox rows commit with the change, or nothing commits. It also finds the derived
/// events: a budget that crossed a threshold, and a subscription with a new price.
/// With the events off, it runs the change as before, with no extra transaction and no extra queries.
/// </summary>
public class FinanceChanges(
    IFinanceEvents events,
    IUnitOfWork unitOfWork,
    IBudgetRepository budgetRepository,
    ICategoryRepository categoryRepository,
    RecurringPayments recurringPayments,
    ReportingCalendar calendar,
    TimeProvider timeProvider)
{
    public bool Enabled => events.Enabled;

    /// <summary>The time for <c>OccurredAt</c>.</summary>
    public DateTimeOffset Now => timeProvider.GetUtcNow();

    public void Raise(Hestia.Contracts.IIntegrationEvent @event) => events.Raise(@event);

    public async Task<T> RunAsync<T>(ChangeEffect effect, Func<CancellationToken, Task<T>> change,
        CancellationToken cancellationToken = default)
    {
        if (!events.Enabled)
        {
            return await change(cancellationToken);
        }

        return await unitOfWork.InTransactionAsync(async ct =>
        {
            // A new attempt after a database failure raises its events again.
            events.Clear();
            var before = effect == ChangeEffect.Spending ? await BudgetLevelsAsync(ct) : null;

            var result = await change(ct);

            if (before is not null)
            {
                await RaiseCrossedThresholdsAsync(before, ct);
            }
            // Stores the events raised after the change's last save, in the same transaction.
            await unitOfWork.SaveChangesAsync(ct);
            return result;
        }, cancellationToken);
    }

    public Task RunAsync(ChangeEffect effect, Func<CancellationToken, Task> change, CancellationToken cancellationToken = default) =>
        RunAsync(effect, async ct =>
        {
            await change(ct);
            return true;
        }, cancellationToken);

    /// <summary>Raises <see cref="TransactionChanged"/> for each transaction, with its category name.</summary>
    public async Task RaiseChangedAsync(IReadOnlyCollection<Transaction> transactions, TransactionChange change,
        CancellationToken cancellationToken)
    {
        if (!events.Enabled || transactions.Count == 0)
        {
            return;
        }
        var names = (await categoryRepository.GetAllAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var now = Now;
        foreach (var transaction in transactions)
        {
            events.Raise(new TransactionChanged(
                transaction.Id,
                change,
                transaction.AccountId,
                DateOnly.FromDateTime(transaction.Date),
                transaction.Description,
                transaction.TransactionType == TransactionType.Credit ? transaction.Amount : -transaction.Amount,
                transaction.CategoryId,
                names.GetValueOrDefault(transaction.CategoryId) ?? transaction.Category?.Name ?? string.Empty,
                now));
        }
    }

    /// <summary>
    /// Raises <see cref="SubscriptionPriceChanged"/> for each recurring payment whose first charge at a new price is
    /// one of <paramref name="newCharges"/> (just imported or added). Call it after the charges are saved.
    /// </summary>
    public async Task RaisePriceChangesAsync(IReadOnlyCollection<Transaction> newCharges, CancellationToken cancellationToken)
    {
        if (!events.Enabled)
        {
            return;
        }
        var ids = newCharges.Where(t => t.Flow == Flow.Spending).Select(t => t.Id).ToHashSet();
        if (ids.Count == 0)
        {
            return;
        }

        var today = calendar.Today();
        var transactions = await recurringPayments.LoadAsync(RecurringPayments.Start(today), today, cancellationToken);
        var dates = transactions.ToDictionary(t => t.Id, t => t.Date);
        var now = Now;
        foreach (var detected in await recurringPayments.DetectAsync(transactions, today, cancellationToken))
        {
            if (detected.Payment.PriceChange is not { } price)
            {
                continue;
            }
            var arrivedNow = detected.Charges.Any(id => ids.Contains(id) && dates.TryGetValue(id, out var date) && date == price.Since);
            if (arrivedNow)
            {
                events.Raise(new SubscriptionPriceChanged(detected.Payment.Name, detected.Payment.Cadence.ToString(),
                    price.From, price.To, DateOnly.FromDateTime(price.Since), now));
            }
        }
    }

    /// <summary>The threshold that each active budget has reached now (0, 80 or 100).</summary>
    private async Task<Dictionary<Guid, int>> BudgetLevelsAsync(CancellationToken cancellationToken) =>
        (await budgetRepository.GetActiveSummariesAsync(calendar.Today(), cancellationToken))
            .ToDictionary(summary => summary.Budget.Id, Level);

    private async Task RaiseCrossedThresholdsAsync(Dictionary<Guid, int> before, CancellationToken cancellationToken)
    {
        var today = calendar.Today();
        var now = Now;
        foreach (var summary in await budgetRepository.GetActiveSummariesAsync(today, cancellationToken))
        {
            var level = Level(summary);
            if (level > before.GetValueOrDefault(summary.Budget.Id))
            {
                var (start, end) = Period(summary.Budget, today);
                events.Raise(new BudgetLimitReached(summary.Budget.Id, summary.Budget.Name, level, summary.Budget.Amount,
                    summary.TotalSpent, start, end, now));
            }
        }
    }

    /// <summary>The highest threshold that the spending reached: 0, 80 or 100.</summary>
    public static int Level(BudgetSummary summary)
    {
        var limit = summary.Budget.Amount;
        if (limit <= 0)
        {
            return 0;
        }
        if (summary.TotalSpent >= limit)
        {
            return BudgetLimitReached.LimitPercent;
        }
        return summary.TotalSpent >= limit * BudgetLimitReached.WarningPercent / 100m ? BudgetLimitReached.WarningPercent : 0;
    }

    /// <summary>The days that the budget counts now, as the budget totals count them.</summary>
    private static (DateOnly? Start, DateOnly? End) Period(Budget budget, DateTime today)
    {
        var month = new DateOnly(today.Year, today.Month, 1);
        return budget.Period switch
        {
            BudgetPeriod.Monthly => (month, month.AddMonths(1).AddDays(-1)),
            BudgetPeriod.Yearly => (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
            BudgetPeriod.Custom => (Day(budget.StartDate), Day(budget.EndDate)),
            _ => (null, null)
        };
    }

    private static DateOnly? Day(DateTime? date) => date is { } value ? DateOnly.FromDateTime(value) : null;
}

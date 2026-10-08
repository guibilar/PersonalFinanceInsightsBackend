using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Application.Reports;
using Finance.Contracts.Inputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services;

/// <summary>
/// The payments that come again and again, found in the spending by merchant (as the category rules name it), for the
/// subscriptions page, the forecast and the reviews alike.
/// </summary>
public class RecurringPayments(ITransactionRepository transactionRepository, ICategoryRuleRepository categoryRuleRepository)
{
    /// <summary>Two years and a month: a yearly payment needs two charges to be seen.</summary>
    private const int LookBackMonths = 25;

    /// <summary>The first day of the spending looked at for payments as of <paramref name="today"/>.</summary>
    public static DateTime Start(DateTime today) =>
        new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-LookBackMonths);

    public Task<List<Transaction>> LoadAsync(DateTime start, DateTime end, CancellationToken cancellationToken) =>
        transactionRepository.FilterNoPaginationAsync(new TransactionFilterDto { StartDate = start, EndDate = end }, cancellationToken);

    public async Task<CategoryMatcher> MatcherAsync(CancellationToken cancellationToken) =>
        new(await categoryRuleRepository.GetAllAsync(cancellationToken));

    public async Task<List<DetectedPayment>> DetectAsync(List<Transaction> transactions, DateTime today, CancellationToken cancellationToken)
    {
        var matcher = await MatcherAsync(cancellationToken);
        return RecurringDetector.Detect(
            transactions.Where(t => t.Flow == Flow.Spending && t.Date <= today)
                .Select(t => new Charge(t.Id, t.Date, t.Amount, MerchantNames.Of(matcher, t.Description), t.Category)),
            today);
    }
}

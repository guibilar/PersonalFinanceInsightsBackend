using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Helpers;

namespace Finance.Application.Import;

/// <summary>
/// Decides a transaction's <see cref="Flow"/>: the flow rule with the longest keyword the description contains, or
/// otherwise what money moving that way on that kind of account usually is.
/// </summary>
public class FlowMatcher
{
    private readonly List<(string Keyword, Flow Flow)> _rules;

    public FlowMatcher(IEnumerable<FlowRule> rules)
    {
        // Longest first, so "INTERNET TRANSFER" beats "TRANSFER"; then by keyword, so ties always go the same way.
        _rules = rules
            .Select(rule => (Keyword: FlowRule.NormalizeKeyword(rule.Keyword), rule.Flow))
            .Where(rule => rule.Keyword.Length > 0)
            .OrderByDescending(rule => rule.Keyword.Length)
            .ThenBy(rule => rule.Keyword, StringComparer.Ordinal)
            .ToList();
    }

    public Flow Match(string description, AccountType account, TransactionType? direction)
    {
        var upper = RuleText.Of(description);
        foreach (var (keyword, flow) in _rules)
        {
            if (upper.Contains(keyword, StringComparison.Ordinal) && IsAllowed(flow, direction))
            {
                return flow;
            }
        }
        return Default(account, direction);
    }

    /// <summary>What a transaction is when no rule matches.</summary>
    public static Flow Default(AccountType account, TransactionType? direction) => (account, direction) switch
    {
        // Rows from before transactions had a direction were never counted.
        (_, null) => Flow.Ignored,
        // Money goes in and out of savings and investments from your other accounts; interest can be made income with a rule.
        (AccountType.Savings or AccountType.Investment, _) => Flow.Transfer,
        (_, TransactionType.Debit) => Flow.Spending,
        (AccountType.CreditCard, TransactionType.Credit) => Flow.Refund,
        _ => Flow.Income
    };

    /// <summary>Spending is only ever money out; income and refunds only money in.</summary>
    public static bool IsAllowed(Flow flow, TransactionType? direction) => flow switch
    {
        Flow.Spending => direction == TransactionType.Debit,
        Flow.Refund or Flow.Income => direction == TransactionType.Credit,
        _ => true
    };
}

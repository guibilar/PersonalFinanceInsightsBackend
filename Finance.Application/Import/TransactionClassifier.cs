using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Import;

/// <summary>Gives transactions their category and flow from the rules (the API's are in the database, the CLI's in import-rules.json).</summary>
public class TransactionClassifier(CategoryMatcher categories, FlowMatcher flows)
{
    /// <summary>Sets what the rules decide, except where it was picked by hand.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool Classify(Transaction transaction, AccountType account)
    {
        var changed = false;
        // Savings and investment movements are transfers, not purchases: their category stays as it is (Other when imported).
        if (!transaction.CategoryLocked && !account.HoldsSavings())
        {
            var category = categories.Categorize(transaction.Description);
            changed |= category != transaction.CategoryId;
            transaction.CategoryId = category;
        }
        if (!transaction.FlowLocked)
        {
            var flow = flows.Match(transaction.Description, account, transaction.TransactionType);
            changed |= flow != transaction.Flow;
            transaction.Flow = flow;
        }
        return changed;
    }
}

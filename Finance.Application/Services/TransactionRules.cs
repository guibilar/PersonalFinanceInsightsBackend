using Finance.Application.Abstractions;
using Finance.Application.Import;

namespace Finance.Application.Services;

/// <summary>The current category and flow rules, as what classifies a transaction: added by hand, imported or re-checked.</summary>
public class TransactionRules(ICategoryRuleRepository categoryRuleRepository, IFlowRuleRepository flowRuleRepository)
{
    public async Task<TransactionClassifier> ClassifierAsync(CancellationToken cancellationToken) =>
        new(new CategoryMatcher(await categoryRuleRepository.GetAllAsync(cancellationToken)),
            await FlowMatcherAsync(cancellationToken));

    public async Task<FlowMatcher> FlowMatcherAsync(CancellationToken cancellationToken) =>
        new(await flowRuleRepository.GetAllAsync(cancellationToken));
}

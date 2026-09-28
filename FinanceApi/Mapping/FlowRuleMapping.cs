using Finance.Contracts.Outputs;
using Finance.Domain.Entities;

namespace FinanceApi.Mapping;

public static class FlowRuleMapping
{
    public static FlowRuleOutputDto ToOutput(this FlowRule rule)
    {
        return new FlowRuleOutputDto { Id = rule.Id, Keyword = rule.Keyword, Flow = rule.Flow };
    }
}

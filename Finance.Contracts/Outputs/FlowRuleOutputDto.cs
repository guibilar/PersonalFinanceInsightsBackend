using Finance.Domain.Enums;

namespace Finance.Contracts.Outputs;

public class FlowRuleOutputDto
{
    public Guid Id { get; set; }
    public required string Keyword { get; set; }
    public Flow Flow { get; set; }
}

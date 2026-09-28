using Finance.Domain.Enums;

namespace Finance.Contracts.Outputs;

public class CategoryRuleOutputDto
{
    public Guid Id { get; set; }
    public required string Keyword { get; set; }
    public Category Category { get; set; }
    public int Position { get; set; }
}

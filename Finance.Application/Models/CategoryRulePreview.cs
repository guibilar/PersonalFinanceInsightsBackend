namespace Finance.Application.Models;

/// <param name="Count">Transactions whose category the rule would change.</param>
/// <param name="Examples">A few of their descriptions.</param>
public record CategoryRulePreview(int Count, List<string> Examples);

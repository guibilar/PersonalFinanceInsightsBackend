using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

/// <summary>
/// One change for many transactions at once: a category, a flow, a budget to count towards, or none. Only what is set
/// changes; a category or flow set this way is kept from the rules, as when edited one by one.
/// </summary>
public class TransactionBulkUpdateDto : IValidatableObject
{
    public const int MaxTransactions = 500;

    [Required, MinLength(1), MaxLength(MaxTransactions)]
    public List<Guid> TransactionIds { get; set; } = [];

    public Guid? CategoryId { get; set; }

    /// <summary>Set on the transactions it fits (money out can't be income or a refund); the others keep theirs.</summary>
    [EnumDataType(typeof(Flow))]
    public Flow? Flow { get; set; }

    /// <summary>Links them to this active budget, moving any linked to another.</summary>
    public Guid? BudgetId { get; set; }

    /// <summary>Unlinks them from their budgets.</summary>
    public bool UnlinkBudget { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CategoryId is null && Flow is null && BudgetId is null && !UnlinkBudget)
        {
            yield return new ValidationResult("Choose what to change: a category, a flow or a budget.", [nameof(TransactionIds)]);
        }
        if (BudgetId is not null && UnlinkBudget)
        {
            yield return new ValidationResult("Link to a budget or unlink, not both.", [nameof(BudgetId)]);
        }
    }
}

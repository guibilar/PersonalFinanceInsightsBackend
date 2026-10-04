using Finance.Domain.Enums;

namespace Finance.Application.Models;

/// <summary>One change for many transactions; see TransactionBulkUpdateDto.</summary>
public record BulkUpdate(IReadOnlyCollection<Guid> TransactionIds, Guid? CategoryId, Flow? Flow, Guid? BudgetId, bool UnlinkBudget,
    Guid? AddTagId = null, Guid? RemoveTagId = null);

/// <summary>How a bulk change went.</summary>
public record BulkUpdateResult(int Updated, int Skipped, IReadOnlyList<string> Notes);

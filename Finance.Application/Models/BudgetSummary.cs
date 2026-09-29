using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Models;

/// <summary>A budget with its spending totalled in the database.</summary>
public record BudgetSummary(Budget Budget, decimal TotalSpent, IReadOnlyList<Category> Categories);

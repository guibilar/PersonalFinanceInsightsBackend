using Finance.Domain.Entities;

namespace Finance.Application.Models;

/// <summary>A budget with its spending totalled in the database.</summary>
public record BudgetSummary(Budget Budget, decimal TotalSpent);

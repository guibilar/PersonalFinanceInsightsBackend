using Finance.Domain.Entities;

namespace Finance.Application.Models;

/// <summary>A budget with its spending totalled in the database, and the categories it counts by itself.</summary>
public record BudgetSummary(Budget Budget, decimal TotalSpent, IReadOnlyList<Category> Categories);

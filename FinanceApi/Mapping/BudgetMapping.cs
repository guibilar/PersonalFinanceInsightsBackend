using Finance.Application.Models;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;

namespace FinanceApi.Mapping;

public static class BudgetMapping
{
    public static Budget ToModel(this BudgetInputDto dto, Guid id = default)
    {
        return new Budget
        {
            Id = id,
            Name = dto.Name.Trim(),
            Description = dto.Description.Trim(),
            Status = dto.Status,
            Amount = dto.Amount,
            Period = dto.Period,
            Categories = dto.CategoryIds.Distinct().Select(id => new BudgetCategory { CategoryId = id }).ToList(),
        };
    }

    public static BudgetOutputDto ToOutput(this BudgetSummary summary)
    {
        var (budget, totalSpent, categories) = summary;
        return new BudgetOutputDto
        {
            Id = budget.Id,
            Name = budget.Name,
            Description = budget.Description,
            Status = budget.Status,
            Period = budget.Period,
            Categories = categories.Select(c => c.ToReference()).ToList(),
            TotalAmount = budget.Amount,
            TotalSpent = totalSpent,
            TotalRemaining = budget.Amount - totalSpent,
            PercentageUsed = budget.Amount > 0 ? totalSpent / budget.Amount * 100 : 0,
        };
    }

    public static BudgetReferenceDto ToReference(this Budget budget)
    {
        return new BudgetReferenceDto { Id = budget.Id, Name = budget.Name, Status = budget.Status };
    }
}

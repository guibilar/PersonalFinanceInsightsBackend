using Finance.Application.Models;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

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
            // Dates belong to a custom period only; the service checks a custom one has both.
            StartDate = dto.Period == BudgetPeriod.Custom ? Day(dto.StartDate) : null,
            EndDate = dto.Period == BudgetPeriod.Custom ? Day(dto.EndDate) : null,
            Merchant = string.IsNullOrWhiteSpace(dto.Merchant) ? null : dto.Merchant.Trim(),
            Categories = dto.CategoryIds.Distinct().Select(id => new BudgetCategory { CategoryId = id }).ToList(),
        };
    }

    // Days are stored as midnight UTC, like transaction dates.
    private static DateTime? Day(DateTime? date) =>
        date is { } d ? DateTime.SpecifyKind(d.Date, DateTimeKind.Utc) : null;

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
            StartDate = budget.StartDate,
            EndDate = budget.EndDate,
            Merchant = budget.Merchant,
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

using FinanceDomain.Models;
using FinanceDto.Inputs;
using FinanceDto.Models;
using FinanceDto.Outputs;

namespace FinanceApi.Converters
{
    public static class BudgetMapping
    {
        public static Budget ToModel(BudgetInputDto dto)
        {
            return new Budget
            {
                Name = dto.Name,
                Description = dto.Description,
                Status = dto.Status,
                Amount = dto.Amount,
            };
        }
        
        public static BudgetOutputDto ToOutput(Budget budget)
        {
            var totalSpent = budget.Transactions != null ? budget.Transactions.Sum(t => t.Amount) : 0;
            var percentageUsed = budget.Amount > 0 ? (totalSpent/budget.Amount)*100 : 0;
            return new BudgetOutputDto
            {
                Id = budget.Id,
                Name = budget.Name,
                Description = budget.Description,
                Status = budget.Status,
                TotalAmount = budget.Amount,
                TotalSpent = totalSpent,
                TotalRemaining =  budget.Amount - totalSpent,
                PercentageUsed = percentageUsed,
            };
        }

        public static List<BudgetOutputDto> ToOutputList(List<Budget>? budgets)
        {
            return budgets == null ? [] : budgets.Select(ToOutput).ToList();
        }
        
        public static PaginatedList<BudgetOutputDto> ToOutputPaginatedList(PaginatedList<Budget>? paginatedInput)
        {
            var toReturn = new PaginatedList<BudgetOutputDto>()
            {
                Items = ToOutputList(paginatedInput.Items),
                Page = paginatedInput.Page,
                PageSize = paginatedInput.PageSize,
                TotalCount = paginatedInput.TotalCount,
            };
            
            return toReturn;
        }
    }
}

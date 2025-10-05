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
                Status = dto.Status
            };
        }
        
        public static BudgetOutputDto ToOutput(Budget budget)
        {
            return new BudgetOutputDto
            {
                Id = budget.Id,
                Name = budget.Name,
                Description = budget.Description,
                Status = budget.Status,
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

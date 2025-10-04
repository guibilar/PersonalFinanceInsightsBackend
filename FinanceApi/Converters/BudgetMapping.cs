using FinanceDomain.Models;
using FinanceDto.Inputs;
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
                Name = budget.Name,
                Description = budget.Description,
                Status = budget.Status,
                Transactions = TransactionMapping.ToOutputList(budget.Transactions)
            };
        }
    }
}

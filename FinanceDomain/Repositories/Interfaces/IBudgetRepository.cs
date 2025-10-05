using FinanceDomain.Models;
using FinanceDto.Inputs;
using FinanceDto.Models;

namespace FinanceDomain.Repositories.Interfaces;

public interface IBudgetRepository : IBaseRepository<Budget>
{
    Task<PaginatedList<Budget>> FilterAsync(BudgetFilterDto? filter);
}

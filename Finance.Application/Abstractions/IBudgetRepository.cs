

using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface IBudgetRepository : IBaseRepository<Budget>
{
    Task<PaginatedList<Budget>> FilterAsync(BudgetFilterDto? filter);
}

using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;

namespace FinanceDomain.Repositories;

public class BudgetRepository(FinanceDbContext dbContext) : BaseRepository<Budget>(dbContext), IBudgetRepository
{
}
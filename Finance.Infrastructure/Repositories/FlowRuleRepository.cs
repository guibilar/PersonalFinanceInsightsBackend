using Finance.Application.Abstractions;
using Finance.Domain.Entities;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Repositories;

public class FlowRuleRepository(FinanceDbContext dbContext) : BaseRepository<FlowRule>(dbContext), IFlowRuleRepository
{
    public Task<List<FlowRule>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return DbContext.FlowRules.OrderBy(r => r.Keyword).ToListAsync(cancellationToken);
    }
}

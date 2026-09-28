using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface IFlowRuleRepository : IBaseRepository<FlowRule>
{
    /// <summary>Tracked, by keyword.</summary>
    Task<List<FlowRule>> GetAllAsync(CancellationToken cancellationToken = default);
}

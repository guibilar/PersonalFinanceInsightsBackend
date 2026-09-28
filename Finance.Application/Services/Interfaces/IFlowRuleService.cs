using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services.Interfaces;

/// <summary>The flow rules used at import and by re-applying the rules (see <see cref="Import.FlowMatcher"/>).</summary>
public interface IFlowRuleService
{
    /// <summary>By keyword.</summary>
    Task<List<FlowRule>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<FlowRule> AddAsync(string keyword, Flow flow, CancellationToken cancellationToken = default);
    Task<FlowRule> UpdateAsync(Guid id, string keyword, Flow flow, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

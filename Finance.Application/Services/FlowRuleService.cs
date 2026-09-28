using Finance.Application.Abstractions;
using Finance.Application.Services.Interfaces;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;

namespace Finance.Application.Services;

public class FlowRuleService(IFlowRuleRepository ruleRepository) : IFlowRuleService
{
    public Task<List<FlowRule>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ruleRepository.GetAllAsync(cancellationToken);

    public async Task<FlowRule> AddAsync(string keyword, Flow flow, CancellationToken cancellationToken = default)
    {
        EnsureNotSpending(flow);
        var rule = new FlowRule { Keyword = FlowRule.NormalizeKeyword(keyword), Flow = flow };
        EnsureUnique(await ruleRepository.GetAllAsync(cancellationToken), rule.Keyword, rule.Id);
        return await ruleRepository.AddAsync(rule, cancellationToken);
    }

    public async Task<FlowRule> UpdateAsync(Guid id, string keyword, Flow flow, CancellationToken cancellationToken = default)
    {
        EnsureNotSpending(flow);
        var rules = await ruleRepository.GetAllAsync(cancellationToken);
        var rule = rules.SingleOrDefault(r => r.Id == id) ?? throw new NotFoundException(nameof(FlowRule), id);
        var normalized = FlowRule.NormalizeKeyword(keyword);
        EnsureUnique(rules, normalized, id);

        rule.Keyword = normalized;
        rule.Flow = flow;
        await ruleRepository.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await ruleRepository.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(FlowRule), id);
        await ruleRepository.RemoveAsync(rule, cancellationToken);
    }

    // Money out is spending whenever no rule matches, so a spending rule could only ever undo another rule.
    private static void EnsureNotSpending(Flow flow)
    {
        if (flow == Flow.Spending)
        {
            throw new InvalidInputException("flow", "Money out is spending unless a rule says otherwise; pick another flow.");
        }
    }

    // The database's unique index is the backstop for concurrent requests; this gives the usual case a clear message.
    private static void EnsureUnique(List<FlowRule> rules, string keyword, Guid id)
    {
        if (rules.Any(r => r.Keyword == keyword && r.Id != id))
        {
            throw new ConflictException($"A rule for \"{keyword}\" already exists.");
        }
    }
}

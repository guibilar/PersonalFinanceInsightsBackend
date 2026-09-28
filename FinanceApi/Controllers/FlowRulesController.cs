using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using FinanceApi.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

/// <summary>
/// The rules that decide whether a transaction is spending, a refund, income, a transfer or ignored: the rule with the
/// longest keyword the description contains wins, and without one money out is spending and money in depends on the
/// account. Changing them affects later imports; <c>POST /api/Transaction/ApplyRules</c> re-applies them.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class FlowRulesController(IFlowRuleService ruleService) : ControllerBase
{
    /// <summary>All rules, by keyword.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FlowRuleOutputDto>>> GetAll(CancellationToken cancellationToken)
    {
        var rules = await ruleService.GetAllAsync(cancellationToken);
        return rules.Select(FlowRuleMapping.ToOutput).ToList();
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FlowRuleOutputDto>> Add(FlowRuleInputDto rule, CancellationToken cancellationToken)
    {
        var result = await ruleService.AddAsync(rule.Keyword, rule.Flow!.Value, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result.ToOutput());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FlowRuleOutputDto>> Update(Guid id, FlowRuleInputDto rule, CancellationToken cancellationToken)
    {
        var result = await ruleService.UpdateAsync(id, rule.Keyword, rule.Flow!.Value, cancellationToken);
        return result.ToOutput();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await ruleService.RemoveAsync(id, cancellationToken);
        return NoContent();
    }
}

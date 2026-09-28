using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using FinanceApi.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

/// <summary>
/// The rules that categorise imported transactions: the first rule (by position) whose keyword the description
/// contains decides. Changing them affects later imports; <c>POST /api/Transaction/ApplyRules</c> re-applies
/// them to stored transactions.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class CategoryRulesController(ICategoryRuleService ruleService) : ControllerBase
{
    /// <summary>All rules, in the order they are checked.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CategoryRuleOutputDto>>> GetAll(CancellationToken cancellationToken)
    {
        var rules = await ruleService.GetAllAsync(cancellationToken);
        return rules.Select(CategoryRuleMapping.ToOutput).ToList();
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryRuleOutputDto>> Add(CategoryRuleInputDto rule, CancellationToken cancellationToken)
    {
        var result = await ruleService.AddAsync(rule.Keyword, rule.Category!.Value, rule.Position, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result.ToOutput());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryRuleOutputDto>> Update(Guid id, CategoryRuleUpdateDto rule, CancellationToken cancellationToken)
    {
        var result = await ruleService.UpdateAsync(id, rule.Keyword, rule.Category!.Value, cancellationToken);
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

    /// <summary>Moves a rule to another position; returns all rules in their new order.</summary>
    [HttpPost("{id:guid}/Move")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<CategoryRuleOutputDto>>> Move(Guid id, CategoryRuleMoveDto move, CancellationToken cancellationToken)
    {
        var rules = await ruleService.MoveAsync(id, move.Position!.Value, cancellationToken);
        return rules.Select(CategoryRuleMapping.ToOutput).ToList();
    }

    /// <summary>How many stored transactions would change category if this rule were added first and the rules re-applied.</summary>
    [HttpPost("Preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CategoryRulePreviewDto>> Preview(CategoryRulePreviewInputDto rule, CancellationToken cancellationToken)
    {
        var preview = await ruleService.PreviewAsync(rule.Keyword, rule.Category!.Value, rule.ExcludeTransactionId, cancellationToken);
        return preview.ToOutput();
    }
}

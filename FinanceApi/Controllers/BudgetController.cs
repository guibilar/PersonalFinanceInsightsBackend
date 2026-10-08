using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using FinanceApi.Auth;
using FinanceApi.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class BudgetController(IBudgetService budgetService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BudgetOutputDto>> Add(BudgetInputDto budget, CancellationToken cancellationToken)
    {
        var result = await budgetService.AddAsync(budget.ToModel(), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Budget.Id }, result.ToOutput());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BudgetOutputDto>> Update(Guid id, BudgetInputDto budget, CancellationToken cancellationToken)
    {
        var result = await budgetService.UpdateAsync(budget.ToModel(id), cancellationToken);
        return result.ToOutput();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BudgetOutputDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await budgetService.GetByIdAsync(id, cancellationToken);
        return result.ToOutput();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await budgetService.RemoveAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("Filter")]
    [ReadOnlyQuery]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedList<BudgetOutputDto>>> Filter(BudgetFilterDto? filter, CancellationToken cancellationToken)
    {
        var result = await budgetService.FilterAsync(filter ?? new BudgetFilterDto(), cancellationToken);
        return result.Map(BudgetMapping.ToOutput);
    }
}

using CrossCutting.Services.Interfaces;
using FinanceApi.Converters;
using FinanceDto.Inputs;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BudgetController(IBudgetService budgetService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] BudgetInputDto budget)
    {
        try
        {
            var budgetModel = BudgetMapping.ToModel(budget);
            var result = await budgetService.AddAsync(budgetModel);
            return Ok(BudgetMapping.ToOutput(result));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while creating the budget.", details = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] BudgetInputDto budget)
    {
        try
        {
            var budgetModel = BudgetMapping.ToModel(budget);
            budgetModel.Id = id;
            var result = await budgetService.UpdateAsync(budgetModel);
            return Ok(BudgetMapping.ToOutput(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Budget not found." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while updating the budget.", details = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var budget = await budgetService.GetByIdAsync(id);
            if (budget == null)
            {
                return NotFound(new { message = "Budget not found." });
            }
            return Ok(BudgetMapping.ToOutput(budget));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while retrieving the budget.", details = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        try
        {
            await budgetService.RemoveAsync(id);
            return Ok(new { message = "Budget deleted successfully." });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Budget not found." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while deleting the budget.", details = ex.Message });
        }
    }
}
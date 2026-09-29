using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using FinanceApi.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

/// <summary>The categories transactions, category rules and budgets use.</summary>
[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    /// <summary>All categories, by name, with how many transactions and rules use each.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CategoryOutputDto>>> GetAll(CancellationToken cancellationToken)
    {
        var categories = await categoryService.GetAllAsync(cancellationToken);
        return categories.Select(CategoryMapping.ToOutput).ToList();
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryOutputDto>> Add(CategoryInputDto category, CancellationToken cancellationToken)
    {
        var result = await categoryService.AddAsync(category.Name, category.Color, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result.ToOutput());
    }

    /// <summary>Renames and/or recolours the category; everything using it follows.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryOutputDto>> Update(Guid id, CategoryInputDto category, CancellationToken cancellationToken)
    {
        var result = await categoryService.UpdateAsync(id, category.Name, category.Color, cancellationToken);
        return result.ToOutput();
    }

    /// <summary>
    /// Deletes the category after moving its transactions (deleted ones included) and rules to <paramref name="moveTo"/>,
    /// Other when omitted; budgets that counted it stop counting it. The built-in ones (Other, Rent) give 409.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(Guid id, [FromQuery] Guid? moveTo, CancellationToken cancellationToken)
    {
        await categoryService.RemoveAsync(id, moveTo, cancellationToken);
        return NoContent();
    }
}

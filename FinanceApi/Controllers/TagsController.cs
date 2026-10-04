using Finance.Application.Models;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

/// <summary>Tags: labels for transactions across categories, any number per transaction.</summary>
[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class TagsController(ITagService tagService) : ControllerBase
{
    /// <summary>All tags, by name, with how many transactions have each.</summary>
    [HttpGet]
    public async Task<List<TagOutputDto>> GetAll(CancellationToken cancellationToken) =>
        (await tagService.GetAllAsync(cancellationToken)).Select(ToOutput).ToList();

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TagOutputDto>> Add(TagInputDto tag, CancellationToken cancellationToken)
    {
        var result = await tagService.AddAsync(tag.Name, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ToOutput(result));
    }

    /// <summary>Renames it; every transaction with it follows.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TagOutputDto>> Rename(Guid id, TagInputDto tag, CancellationToken cancellationToken) =>
        ToOutput(await tagService.RenameAsync(id, tag.Name, cancellationToken));

    /// <summary>Deletes it; its transactions just lose it.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await tagService.RemoveAsync(id, cancellationToken);
        return NoContent();
    }

    private static TagOutputDto ToOutput(TagSummary summary) =>
        new() { Id = summary.Tag.Id, Name = summary.Tag.Name, TransactionCount = summary.TransactionCount };
}

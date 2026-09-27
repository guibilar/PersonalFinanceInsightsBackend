using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using FinanceApi.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class TransactionController(ITransactionService transactionService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TransactionOutputDto>> Add(TransactionInputDto transaction, CancellationToken cancellationToken)
    {
        var result = await transactionService.AddAsync(transaction.ToModel(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result.ToOutput());
    }

    [HttpPost("SyncTransactionsByFile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SyncTransactionsByFile(CancellationToken cancellationToken)
    {
        await transactionService.SyncTransactionsByFileAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("GetAll")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<TransactionOutputDto>>> GetAll(CancellationToken cancellationToken)
    {
        var transactions = await transactionService.GetAllAsync(cancellationToken);
        return transactions.Select(TransactionMapping.ToOutput).ToList();
    }

    [HttpPost("Filter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedList<TransactionOutputDto>>> Filter(TransactionFilterDto? filter, CancellationToken cancellationToken)
    {
        filter ??= new TransactionFilterDto();
        if (filter.StartDate.HasValue)
        {
            filter.StartDate = DateTime.SpecifyKind(filter.StartDate.Value.Date, DateTimeKind.Utc);
        }
        if (filter.EndDate.HasValue)
        {
            // The repository compares with "<", so move to the next day to include the whole end date.
            filter.EndDate = DateTime.SpecifyKind(filter.EndDate.Value.Date.AddDays(1), DateTimeKind.Utc);
        }

        var result = await transactionService.FilterAsync(filter, cancellationToken);
        return result.Map(TransactionMapping.ToOutput);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await transactionService.RemoveAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("LinkTransaction")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LinkTransaction(TransactionLinkInputDto link, CancellationToken cancellationToken)
    {
        await transactionService.LinkTransactionToBudgetAsync(link.BudgetId, link.TransactionId, cancellationToken);
        return NoContent();
    }

    [HttpPost("UnlinkTransaction/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlinkTransaction(Guid id, CancellationToken cancellationToken)
    {
        await transactionService.UnlinkTransactionFromBudgetAsync(id, cancellationToken);
        return NoContent();
    }
}

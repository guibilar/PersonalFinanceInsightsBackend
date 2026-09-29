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
    private const long MaxStatementBytes = 5 * 1024 * 1024;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TransactionOutputDto>> Add(TransactionInputDto transaction, CancellationToken cancellationToken)
    {
        var result = await transactionService.AddAsync(transaction.ToModel(), transaction.AllowDuplicate, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result.ToOutput());
    }

    /// <summary>
    /// Imports a CIBC CSV export into an account. Re-importing an overlapping statement only adds new rows. A file that
    /// doesn't look like the account's is refused with 409 (code "account-mismatch") unless <c>confirmed</c> is set.
    /// </summary>
    [HttpPost("Import")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxStatementBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ImportResultDto>> Import(IFormFile file, [FromForm] StatementImportDto import, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "The statement file is empty.");
            return ValidationProblem(ModelState);
        }

        await using var stream = file.OpenReadStream();
        var result = await transactionService.ImportAsync(stream, import.AccountId!.Value, import.Confirmed, cancellationToken);
        return new ImportResultDto { Read = result.Read, Added = result.Added, Skipped = result.Skipped };
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
            // The last representable day has no next day: it means no upper bound.
            var endDate = filter.EndDate.Value.Date;
            filter.EndDate = endDate < DateTime.MaxValue.Date
                ? DateTime.SpecifyKind(endDate.AddDays(1), DateTimeKind.Utc)
                : null;
        }

        var result = await transactionService.FilterAsync(filter, cancellationToken);
        return result.Map(TransactionMapping.ToOutput);
    }

    /// <summary>
    /// Corrects a transaction's description, category and flow (a changed category or flow is then locked against
    /// rule changes).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionOutputDto>> Update(Guid id, TransactionUpdateDto update, CancellationToken cancellationToken)
    {
        var transaction = await transactionService.UpdateAsync(id, update.Description, update.Category!.Value, update.Flow, cancellationToken);
        return transaction.ToOutput();
    }

    /// <summary>
    /// Re-applies the current category and flow rules (see CategoryRulesController and FlowRulesController) to stored
    /// transactions.
    /// </summary>
    [HttpPost("ApplyRules")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<RulesResultDto>> ApplyRules(CancellationToken cancellationToken)
    {
        var result = await transactionService.ApplyRulesAsync(cancellationToken);
        return new RulesResultDto { Checked = result.Checked, Updated = result.Updated };
    }

    /// <summary>
    /// Deletes a transaction from lists, reports and budgets. It is kept hidden rather than removed, so
    /// re-importing a statement that contains it doesn't add it back; <see cref="Restore"/> undoes it.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await transactionService.HideAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Brings back a deleted (hidden) transaction.</summary>
    [HttpPost("{id:guid}/Restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        await transactionService.RestoreAsync(id, cancellationToken);
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

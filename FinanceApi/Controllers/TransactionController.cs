using Finance.Application.Models;
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
public class TransactionController(ITransactionService transactionService, IStatementImportService statementImportService) : ControllerBase
{
    private const long MaxStatementBytes = 5 * 1024 * 1024;

    // The limit covers the whole multipart body: room for the form's own parts, so a file of up to the 5 MB the clients
    // allow isn't refused for a few hundred bytes of headers.
    private const long MaxRequestBytes = MaxStatementBytes + 64 * 1024;

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
    [RequestSizeLimit(MaxRequestBytes)]
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
        if (file.Length > MaxStatementBytes)
        {
            return Problem(statusCode: StatusCodes.Status413PayloadTooLarge, detail: "The statement is larger than 5 MB.");
        }

        await using var stream = file.OpenReadStream();
        var result = await statementImportService.ImportAsync(stream, import.AccountId!.Value, import.Confirmed, cancellationToken);
        return result.ToOutput();
    }

    [HttpPost("Filter")]
    [ReadOnlyQuery]
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
        var transaction = await transactionService.UpdateAsync(id, update.Description, update.CategoryId!.Value, update.Flow, update.Note, update.TagIds, cancellationToken);
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
        return result.ToOutput();
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

    /// <summary>
    /// Sets a category, a flow or a budget on many transactions at once (up to 500). A flow that doesn't fit a
    /// transaction's direction leaves that one's as it is; the notes say how many.
    /// </summary>
    [HttpPost("Bulk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BulkUpdateResultDto>> Bulk(TransactionBulkUpdateDto update, CancellationToken cancellationToken)
    {
        var result = await transactionService.BulkUpdateAsync(
            new BulkUpdate(update.TransactionIds, update.CategoryId, update.Flow, update.BudgetId, update.UnlinkBudget, update.AddTagId, update.RemoveTagId), cancellationToken);
        return result.ToOutput();
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

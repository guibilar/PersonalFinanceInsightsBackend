using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using FinanceApi.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

/// <summary>The bank accounts and cards statements are imported into.</summary>
[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class AccountsController(IAccountService accountService) : ControllerBase
{
    /// <summary>All accounts, by kind (chequing, savings, credit card) then name.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AccountOutputDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accounts = await accountService.GetAllAsync(cancellationToken);
        return accounts.Select(AccountMapping.ToOutput).ToList();
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountOutputDto>> Add(AccountInputDto account, CancellationToken cancellationToken)
    {
        var result = await accountService.AddAsync(account.Name, account.Type!.Value, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result.ToOutput());
    }

    /// <summary>Renames the account; its kind can't change.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountOutputDto>> Rename(Guid id, AccountUpdateDto account, CancellationToken cancellationToken)
    {
        var result = await accountService.RenameAsync(id, account.Name, cancellationToken);
        return result.ToOutput();
    }

    /// <summary>Deletes an account without transactions; one with transactions (deleted ones included) gives 409.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await accountService.RemoveAsync(id, cancellationToken);
        return NoContent();
    }
}

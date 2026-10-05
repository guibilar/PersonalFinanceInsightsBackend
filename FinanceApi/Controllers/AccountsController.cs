using Finance.Application.Models;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using FinanceApi.Mapping;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

/// <summary>The bank accounts, cards and investments statements are imported into, and the balances entered for them.</summary>
[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class AccountsController(IAccountService accountService, ReportingCalendar calendar) : ControllerBase
{
    /// <summary>All accounts, by kind (chequing, savings, credit card, investment) then name.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AccountOutputDto>>> GetAll(CancellationToken cancellationToken)
    {
        var accounts = await accountService.GetAllAsync(cancellationToken);
        var year = calendar.Today().Year;
        return accounts.Select(a => a.ToOutput(year)).ToList();
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountOutputDto>> Add(AccountInputDto account, CancellationToken cancellationToken)
    {
        var result = await accountService.AddAsync(account.Type!.Value, Details(account), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result.ToOutput(calendar.Today().Year));
    }

    /// <summary>Renames the account, corrects its statement format, or sets its plan and room; its kind can't change.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountOutputDto>> Update(Guid id, AccountUpdateDto account, CancellationToken cancellationToken)
    {
        var result = await accountService.UpdateAsync(id, Details(account), cancellationToken);
        return result.ToOutput(calendar.Today().Year);
    }

    /// <summary>Deletes an account without transactions (and its balances); one with transactions (deleted ones included) gives 409.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await accountService.RemoveAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>The balances entered for the account, newest first.</summary>
    [HttpGet("{id:guid}/Balances")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AccountBalanceOutputDto>>> GetBalances(Guid id, CancellationToken cancellationToken)
    {
        var balances = await accountService.GetBalancesAsync(id, cancellationToken);
        return balances.Select(b => b.ToOutput()).ToList();
    }

    /// <summary>Its balance at the end of a day, replacing one entered for that day before.</summary>
    [HttpPost("{id:guid}/Balances")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountBalanceOutputDto>> SetBalance(Guid id, AccountBalanceInputDto balance, CancellationToken cancellationToken)
    {
        var result = await accountService.SetBalanceAsync(id, balance.Date!.Value, balance.Amount!.Value, cancellationToken);
        return result.ToOutput();
    }

    [HttpDelete("{id:guid}/Balances/{balanceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveBalance(Guid id, Guid balanceId, CancellationToken cancellationToken)
    {
        await accountService.RemoveBalanceAsync(id, balanceId, cancellationToken);
        return NoContent();
    }

    private static AccountDetails Details(AccountUpdateDto account) =>
        new(account.Name, account.Format, account.Plan, account.ContributionRoom, account.ContributionRoomYear);
}

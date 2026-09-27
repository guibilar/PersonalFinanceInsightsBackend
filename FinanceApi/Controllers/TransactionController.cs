using Microsoft.AspNetCore.Mvc;
using FinanceApi.Mapping;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;

namespace FinanceApi.Controllers{
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionController(ITransactionService transactionService) : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] TransactionInputDto transaction)
        {
            try
            {
                var transactionModel = TransactionMapping.ToModel(transaction);
                var result = await transactionService.AddAsync(transactionModel);
                return Ok(TransactionMapping.ToOutput(result));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Transaction already exists"))
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [Route("SyncTransactionsByFile")]
        [HttpPost]
        public async Task<IActionResult> SyncTransactionsByFile()
        {
            await transactionService.SyncTransactionsByFileAsync();
            return Ok();
        }

        [Route("GetAll")]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await transactionService.GetAllAsync());
        }

        [Route("Filter")]
        [HttpPost]
        public async Task<IActionResult> Filter([FromBody] TransactionFilterDto? filter)
        {
            if (filter is { StartDate: not null })
            {
                filter.StartDate = DateTime.SpecifyKind(filter.StartDate.Value.Date, DateTimeKind.Utc);
            }
            if (filter is { EndDate: not null })
            {
                // The repository compares with "<", so move to the next day to include the whole end date.
                filter.EndDate = DateTime.SpecifyKind(filter.EndDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            }
            var filteredTransactions = await transactionService.FilterAsync(filter);
            return Ok(TransactionMapping.ToOutputPaginatedList(filteredTransactions));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Remove(Guid id)
        {
            try
            {
                await transactionService.RemoveAsync(id);
                return Ok(new { message = "Transaction deleted successfully." });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = "Transaction not found." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while deleting the transaction.", details = ex.Message });
            }
        }
        
        [Route("LinkTransaction")]
        [HttpPost]
        public async Task<IActionResult> LinkTransaction([FromBody] TransactionLinkInputDto transactionLinkInputDto)
        {
            try
            {
                await transactionService.LinkTransactionToBudget(transactionLinkInputDto.BudgetId, transactionLinkInputDto.TransactionId);
                return Ok();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while linking transaction.", details = ex.Message });
            }
        }
    
        [Route("UnlinkTransaction/{id:guid}")]
        [HttpPost]
        public async Task<IActionResult> UnlinkTransaction(Guid id)
        {
            try
            {
                await transactionService.UnlinkTransactionFromBudget(id);
                return Ok();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while unlinking transaction.", details = ex.Message });
            }
        }
    }
}
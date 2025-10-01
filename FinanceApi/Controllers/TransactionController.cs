using CrossCutting.Services;
using FinanceApi.Converters;
using FinanceDomain.Models;
using FinanceDto.Inputs;
using Microsoft.AspNetCore.Mvc;
using TransactionInputDto = FinanceDto.Inputs.TransactionInputDto;

namespace FinanceApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionController(ITransactionService transactionService) : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] TransactionInputDto transaction)
        {
            try
            {
                var transactionModel = TransactionConverter.ConvertToModel(transaction);
                var result = await transactionService.AddAsync(transactionModel);
                return Ok(result);
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
            return Ok(await transactionService.FilterAsync(filter));
        }
    }
}
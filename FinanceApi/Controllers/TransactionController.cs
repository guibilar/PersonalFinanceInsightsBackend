using CrossCutting.Services;
using FinanceDomain.Models;
using FinanceDto.Inputs;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionController(ITransactionService transactionService) : ControllerBase
    {
        // POST api/<TransactionController>
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] Transaction transactionInputDto)
        {
            return Ok(await transactionService.AddAsync(transactionInputDto));
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

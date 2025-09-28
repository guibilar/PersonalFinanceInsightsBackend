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
        public IActionResult Add([FromBody] Transaction transactionInputDto)
        {
            return Ok(transactionService.Add(transactionInputDto));
        }
        
        [Route("SyncTransactionsByFile")]
        [HttpPost]
        public IActionResult SyncTransactionsByFile()
        {
            transactionService.SyncTransactionsByFile();
            return Ok();
        }
        
        [Route("GetAll")]
        [HttpGet]
        public IActionResult GetAll()
        {
            return Ok(transactionService.GetAll());
        }
        
        [Route("Filter")]
        [HttpPost]
        public IActionResult Filter([FromBody] TransactionFilterDto? filter)
        {
            return Ok(transactionService.Filter(filter));
        }
    }
}

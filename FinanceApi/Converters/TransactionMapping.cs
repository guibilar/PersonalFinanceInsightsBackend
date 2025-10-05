using FinanceDomain.Models;
using FinanceDto.Inputs;
using FinanceDto.Models;
using FinanceDto.Outputs;

namespace FinanceApi.Converters
{
    public static class TransactionMapping
    {
        public static Transaction ToModel(TransactionInputDto dto)
        {
            return new Transaction
            {
                Date = DateTime.SpecifyKind(dto.Date, DateTimeKind.Utc),
                Description = dto.Description,
                Amount = dto.Amount,
                Category = dto.Category,
                TransactionType = dto.TransactionType,
                Account = dto.Account,
                Hash = dto.Hash,
            };
        }
        
        public static TransactionOutputDto ToOutput(Transaction transaction)
        {
            return new TransactionOutputDto
            {
                Id = transaction.Id,
                Date = DateTime.SpecifyKind(transaction.Date, DateTimeKind.Utc),
                Description = transaction.Description,
                Amount = transaction.Amount,
                Category = transaction.Category,
                TransactionType = transaction.TransactionType,
                Account = transaction.Account,
            };
        }

        public static List<TransactionOutputDto> ToOutputList(List<Transaction>? transactions)
        {
            return transactions == null ? [] : transactions.Select(ToOutput).ToList();
        }
        
        public static PaginatedList<TransactionOutputDto> ToOutputPaginatedList(PaginatedList<Transaction>? paginatedInput)
        {
            var toReturn = new PaginatedList<TransactionOutputDto>()
            {
                Items = ToOutputList(paginatedInput.Items),
                Page = paginatedInput.Page,
                PageSize = paginatedInput.PageSize,
                TotalCount = paginatedInput.TotalCount,
            };
            
            return toReturn;
        }
    }
}

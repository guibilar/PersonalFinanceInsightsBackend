using FinanceDomain.Models;
using FinanceDto.Inputs;

namespace FinanceApi.Converters
{
    public static class TransactionConverter
    {
        public static Transaction ConvertToModel(TransactionInputDto dto)
        {
            return new Transaction
            {
                Date = dto.Date,
                Description = dto.Description,
                Amount = dto.Amount,
                Category = dto.Category,
                TransactionType = dto.TransactionType,
                Account = dto.Account
            };
        }
    }
}

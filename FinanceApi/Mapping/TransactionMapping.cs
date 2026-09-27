

using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Helpers;

namespace FinanceApi.Mapping{
    public static class TransactionMapping
    {
        public static Transaction ToModel(TransactionInputDto dto)
        {
            var date = DateTime.SpecifyKind(dto.Date, DateTimeKind.Utc);
            return new Transaction
            {
                Date = date,
                Description = dto.Description,
                Amount = dto.Amount,
                Category = dto.Category,
                TransactionType = dto.TransactionType,
                Account = dto.Account,
                // Computed server-side, same as the CSV sync, so duplicates are detected consistently.
                Hash = TransactionHashHelper.GenerateTransactionHash(dto.Amount, date, dto.Description),
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
                BudgetId = transaction.BudgetId.HasValue ? transaction.BudgetId.Value : Guid.Empty,
                Budget = transaction.Budget != null ? BudgetMapping.ToOutput(transaction.Budget) : null
            };
        }

        public static List<TransactionOutputDto> ToOutputList(List<Transaction>? transactions)
        {
            return transactions == null ? [] : transactions.Select(ToOutput).ToList();
        }

        public static PaginatedList<TransactionOutputDto> ToOutputPaginatedList(
            PaginatedList<Transaction>? paginatedInput)
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
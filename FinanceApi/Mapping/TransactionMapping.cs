using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Helpers;

namespace FinanceApi.Mapping;

public static class TransactionMapping
{
    // Called after model validation, which guarantees the required values are present.
    public static Transaction ToModel(this TransactionInputDto dto)
    {
        var date = DateTime.SpecifyKind(dto.Date!.Value, DateTimeKind.Utc);
        return new Transaction
        {
            Date = date,
            Description = dto.Description,
            Amount = dto.Amount,
            Category = dto.Category!.Value,
            TransactionType = dto.TransactionType!.Value,
            Account = dto.Account,
            // Computed server-side, same as the CSV sync, so duplicates are detected consistently.
            Hash = TransactionHashHelper.GenerateTransactionHash(dto.Amount, date, dto.Description),
        };
    }

    public static TransactionOutputDto ToOutput(this Transaction transaction)
    {
        return new TransactionOutputDto
        {
            Id = transaction.Id,
            Date = DateTime.SpecifyKind(transaction.Date, DateTimeKind.Utc),
            Description = transaction.Description,
            Amount = transaction.Amount,
            Category = transaction.Category,
            CategoryLocked = transaction.CategoryLocked,
            TransactionType = transaction.TransactionType,
            Account = transaction.Account,
            BudgetId = transaction.BudgetId,
            Budget = transaction.Budget?.ToReference()
        };
    }
}

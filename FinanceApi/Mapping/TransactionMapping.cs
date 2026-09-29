using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Helpers;

namespace FinanceApi.Mapping;

public static class TransactionMapping
{
    // Called after model validation, which guarantees the required values are present.
    public static Transaction ToModel(this TransactionInputDto dto)
    {
        // Stored like imported rows: the calendar date at midnight UTC.
        var date = DateTime.SpecifyKind(dto.Date!.Value.Date, DateTimeKind.Utc);
        return new Transaction
        {
            Date = date,
            Description = dto.Description,
            Amount = dto.Amount,
            CategoryId = dto.CategoryId ?? Category.OtherId,
            CategoryLocked = dto.CategoryId is not null,
            Flow = dto.Flow ?? default,
            FlowLocked = dto.Flow is not null,
            TransactionType = dto.TransactionType!.Value,
            AccountId = dto.AccountId!.Value,
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
            Category = transaction.Category.ToReference(),
            CategoryLocked = transaction.CategoryLocked,
            Hidden = transaction.Hidden,
            TransactionType = transaction.TransactionType,
            AccountId = transaction.AccountId,
            AccountName = transaction.Account?.Name ?? string.Empty,
            AccountType = transaction.Account?.Type ?? default,
            Flow = transaction.Flow,
            FlowLocked = transaction.FlowLocked,
            BudgetId = transaction.BudgetId,
            Budget = transaction.Budget?.ToReference()
        };
    }
}

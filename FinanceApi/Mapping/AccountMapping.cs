using Finance.Application.Models;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;

namespace FinanceApi.Mapping;

public static class AccountMapping
{
    public static AccountOutputDto ToOutput(this AccountSummary summary, int year)
    {
        var account = summary.Account;
        return new AccountOutputDto
        {
            Id = account.Id,
            Name = account.Name,
            Type = account.Type,
            Format = account.Format,
            TransactionCount = summary.TransactionCount,
            LastTransactionDate = summary.LastTransactionDate,
            LastImportedAt = account.LastImportedAt,
            Plan = account.Plan,
            ContributionRoom = account.ContributionRoom,
            ContributionRoomYear = account.ContributionRoomYear,
            DepositedThisYear = summary.Deposited,
            WithdrawnThisYear = summary.Withdrawn,
            RoomLeft = account.ContributionRoom is { } room && account.ContributionRoomYear == year ? room - summary.Deposited : null,
            Balance = summary.Balance?.ToOutput(),
        };
    }

    public static AccountBalanceOutputDto ToOutput(this AccountBalance balance) => new()
    {
        Id = balance.Id,
        Date = balance.Date,
        Amount = balance.Amount,
    };
}

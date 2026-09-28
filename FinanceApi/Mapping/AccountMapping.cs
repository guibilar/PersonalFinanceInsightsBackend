using Finance.Application.Models;
using Finance.Contracts.Outputs;

namespace FinanceApi.Mapping;

public static class AccountMapping
{
    public static AccountOutputDto ToOutput(this AccountSummary summary)
    {
        return new AccountOutputDto
        {
            Id = summary.Account.Id,
            Name = summary.Account.Name,
            Type = summary.Account.Type,
            TransactionCount = summary.TransactionCount
        };
    }
}

using Finance.Application.Import;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace FinanceTests;

/// <summary>The accounts every database starts with, one per kind.</summary>
public static class TestAccounts
{
    public static Guid Id(AccountType type) => type switch
    {
        AccountType.Chequing => Account.DefaultChequingId,
        AccountType.Savings => Account.DefaultSavingsId,
        _ => Account.DefaultCreditCardId
    };

    /// <summary>A stored row's flow when no flow rule matched it.</summary>
    public static Flow DefaultFlow(AccountType type, TransactionType direction) => FlowMatcher.Default(type, direction);
}

using FinanceDomain.Models;
using FinanceDto.Enums;

namespace CrossCutting.Helpers;

public static class TransactionTypeHelper
{
    public static TransactionType DefineTransactionType(int csvIndex)
    {
        return csvIndex switch
        {
            2 => TransactionType.Debit,
            3 => TransactionType.Credit,
            _ => throw new ArgumentException($"Invalid csv index: {csvIndex}")
        };
    }
}
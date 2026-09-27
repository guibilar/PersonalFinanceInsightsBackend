

using Finance.Domain.Enums;

namespace Finance.Infrastructure.Import;

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
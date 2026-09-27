
namespace Finance.Contracts.Inputs;

public static class PagingLimits
{
    // The frontend loads every active budget for its link dropdown in one page.
    public const int MaxPageSize = 1000;

    // Keeps (page - 1) * pageSize inside an int; far more pages than any real data set has.
    public const int MaxPage = 1_000_000;
}

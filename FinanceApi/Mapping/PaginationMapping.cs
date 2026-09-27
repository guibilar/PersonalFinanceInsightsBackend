using Finance.Domain.Common;

namespace FinanceApi.Mapping;

public static class PaginationMapping
{
    public static PaginatedList<TOut> Map<TIn, TOut>(this PaginatedList<TIn> page, Func<TIn, TOut> map)
    {
        return new PaginatedList<TOut>
        {
            Items = page.Items.Select(map).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount
        };
    }
}

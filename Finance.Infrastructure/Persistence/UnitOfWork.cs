using Finance.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Persistence;

public class UnitOfWork(FinanceDbContext dbContext) : IUnitOfWork
{
    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        // The in-memory test provider has no transactions; inside an open transaction, the work joins it.
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null)
        {
            return work(cancellationToken);
        }

        // The retrying execution strategy allows an explicit transaction only when it can run all of it again.
        return dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}

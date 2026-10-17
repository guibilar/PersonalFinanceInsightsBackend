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
        var attempt = 0;
        return dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            // Another attempt starts from the database: the failed one's saves were rolled back, but their entities are
            // still tracked as saved, so changing them again would store nothing.
            if (attempt++ > 0) dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}

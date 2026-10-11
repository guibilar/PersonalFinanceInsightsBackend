namespace Finance.Application.Abstractions;

/// <summary>
/// One database transaction around a change that has several saves, so the change and its events (T04) commit together.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> in one transaction and commits it. Inside a transaction that is open already, it
    /// joins that one. After a transient database failure the whole <paramref name="work"/> can run again, so it must
    /// start from the same input (for example, the rows of a statement that was read before).
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

    /// <summary>Saves what is tracked: also a save with no change, which stores the events raised since the last save.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

using Finance.Application.Import;

namespace Finance.Application.Services.Interfaces;

public interface IStatementImportService
{
    /// <summary>
    /// Imports the statement into the account, all rows or none. Unless <paramref name="confirmed"/>, a statement that
    /// doesn't look like the account's is refused with <see cref="Domain.Exceptions.AccountMismatchException"/>.
    /// </summary>
    Task<ImportResult> ImportAsync(Stream statement, Guid accountId, bool confirmed, CancellationToken cancellationToken = default);
}

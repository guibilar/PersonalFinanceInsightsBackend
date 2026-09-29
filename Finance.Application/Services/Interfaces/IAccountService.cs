using Finance.Application.Models;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services.Interfaces;

/// <summary>The accounts statements are imported into. Names are unique ignoring case; the kind never changes.</summary>
public interface IAccountService
{
    /// <summary>By kind, then name.</summary>
    Task<List<AccountSummary>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AccountSummary> AddAsync(string name, AccountType type, StatementFormat format, CancellationToken cancellationToken = default);

    /// <param name="format">The new statement format; unchanged when null.</param>
    Task<AccountSummary> UpdateAsync(Guid id, string name, StatementFormat? format, CancellationToken cancellationToken = default);

    /// <summary>Only an account without transactions (deleted ones included) can be deleted.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

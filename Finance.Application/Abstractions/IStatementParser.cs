using Finance.Application.Import;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Abstractions;

/// <summary>Turns a bank statement export into (unsaved, un-hashed) transactions.</summary>
public interface IStatementParser
{
    /// <param name="categories">The category rules to apply (the API's are stored in the database, the CLI's in import-rules.json).</param>
    List<Transaction> Parse(Stream statement, AccountType account, CategoryMatcher categories);
}

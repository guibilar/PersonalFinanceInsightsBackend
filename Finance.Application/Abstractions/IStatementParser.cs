using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Abstractions;

/// <summary>Turns a bank statement export into (unsaved, un-hashed) transactions.</summary>
public interface IStatementParser
{
    List<Transaction> Parse(Stream statement, AccountType account);
}

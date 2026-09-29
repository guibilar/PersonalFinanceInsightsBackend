using Finance.Application.Import;
using Finance.Domain.Enums;

namespace Finance.Application.Abstractions;

/// <summary>Turns a bank statement export into its rows, before any rules are applied.</summary>
public interface IStatementParser
{
    /// <summary>The layout this parser reads; an account's statements are read by the parser for its format.</summary>
    StatementFormat Format { get; }

    /// <exception cref="Domain.Exceptions.InvalidStatementException">The file isn't in this format.</exception>
    ParsedStatement Parse(Stream statement);
}

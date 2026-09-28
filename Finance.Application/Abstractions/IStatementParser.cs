using Finance.Application.Import;

namespace Finance.Application.Abstractions;

/// <summary>Turns a bank statement export into its rows, before any rules are applied.</summary>
public interface IStatementParser
{
    ParsedStatement Parse(Stream statement);
}

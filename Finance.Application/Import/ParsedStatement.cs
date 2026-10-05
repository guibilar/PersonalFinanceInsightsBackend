using Finance.Domain.Entities;

namespace Finance.Application.Import;

/// <param name="Transactions">
/// Every movement, in file order: unsaved, without account, hash or flow, and in category <c>Other</c>.
/// </param>
/// <param name="IsCreditCardExport">The file itself says it comes from a credit card (CIBC card exports carry the card number).</param>
/// <param name="ClosingBalance">What the account was worth at the end of the statement's period, where the statement says.</param>
public record ParsedStatement(List<Transaction> Transactions, bool IsCreditCardExport, StatementBalance? ClosingBalance = null);

/// <param name="Date">The last day of the statement's period.</param>
/// <param name="Amount">The account's whole value that day (for an investment account: its holdings at market value and its cash).</param>
public record StatementBalance(DateTime Date, decimal Amount);

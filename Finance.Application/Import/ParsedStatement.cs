using Finance.Domain.Entities;

namespace Finance.Application.Import;

/// <param name="Transactions">
/// Every movement, in file order: unsaved, without account, hash or flow, and in category <c>Other</c>.
/// </param>
/// <param name="IsCreditCardExport">The file itself says it comes from a credit card (CIBC card exports carry the card number).</param>
public record ParsedStatement(List<Transaction> Transactions, bool IsCreditCardExport);

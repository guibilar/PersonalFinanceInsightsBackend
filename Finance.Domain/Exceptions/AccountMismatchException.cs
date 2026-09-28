namespace Finance.Domain.Exceptions;

/// <summary>
/// A statement that doesn't look like it belongs to the account it is imported into. Returned to clients as 409 with
/// code <see cref="Code"/>; importing again with confirmation goes ahead.
/// </summary>
public class AccountMismatchException(string message) : ConflictException(message)
{
    public const string Code = "account-mismatch";
}

namespace Finance.Domain.Exceptions;

/// <summary>
/// A request value that passed model validation but can't be used (it depends on stored data). Returned to clients as
/// 400, shaped like model validation with the message under <see cref="Field"/>.
/// </summary>
public class InvalidInputException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

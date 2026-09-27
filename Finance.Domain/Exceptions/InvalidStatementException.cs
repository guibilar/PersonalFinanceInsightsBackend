namespace Finance.Domain.Exceptions;

/// <summary>An uploaded statement that can't be read (wrong format or not a CSV at all). Returned to clients as 400.</summary>
public class InvalidStatementException(string message) : Exception(message);

namespace Finance.Domain.Exceptions;

/// <summary>The request conflicts with the current state. Returned to clients as 409.</summary>
public class ConflictException(string message) : Exception(message);

namespace Finance.Domain.Exceptions;

/// <summary>Saving would store a second copy of a record that must be unique. Returned to clients as 409.</summary>
public class DuplicateEntityException(string message) : ConflictException(message);

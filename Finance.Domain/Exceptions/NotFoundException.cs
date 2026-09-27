namespace Finance.Domain.Exceptions;

/// <summary>The requested entity does not exist. Returned to clients as 404.</summary>
public class NotFoundException(string entity, Guid id)
    : Exception($"{entity} with id {id} was not found.");

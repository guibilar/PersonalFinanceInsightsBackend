namespace Finance.Contracts.Outputs;

/// <summary>A tag as transactions show it.</summary>
public class TagReferenceDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
}

public class TagOutputDto : TagReferenceDto
{
    /// <summary>How many transactions (deleted ones aside) have it.</summary>
    public int TransactionCount { get; set; }
}

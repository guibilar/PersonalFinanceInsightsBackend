namespace Finance.Contracts.Outputs;

/// <summary>A category as other resources show it: enough to display it without looking it up.</summary>
public class CategoryReferenceDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>"#RRGGBB".</summary>
    public required string Color { get; set; }
}

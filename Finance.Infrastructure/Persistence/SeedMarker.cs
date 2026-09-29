namespace Finance.Infrastructure.Persistence;

/// <summary>
/// Initial data that was copied into the database once, so it is not copied again after the user deleted it.
/// </summary>
public class SeedMarker
{
    public const string CategoryRules = "CategoryRules";
    public const string FlowRules = "FlowRules";

    /// <summary>Marker for a rule pack (see <see cref="Finance.Application.Import.RulePack"/>): one per pack name.</summary>
    public static string RulePack(string name) => "RulePack:" + name;

    public string Name { get; set; } = string.Empty;
    public DateTimeOffset SeededUtc { get; set; }
}

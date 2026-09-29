namespace Finance.Domain.Entities;

/// <summary>
/// The categories every database starts with: before categories were data, these were the only ones, and a migration
/// moved the stored transactions, rules and budgets onto them. After that they are ordinary data (renamed, recoloured,
/// deleted), apart from <see cref="Category.OtherId"/> and <see cref="Category.RentId"/>, which can't be deleted.
/// </summary>
public static class DefaultCategories
{
    public static readonly Guid Dining = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b01");
    public static readonly Guid Entertainment = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b02");
    public static readonly Guid Groceries = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b03");
    public static readonly Guid Health = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b04");
    public static readonly Guid Other = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b05");
    public static readonly Guid Pet = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b06");
    public static readonly Guid Rent = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b07");
    public static readonly Guid Shopping = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b08");
    public static readonly Guid Subscriptions = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b09");
    public static readonly Guid Transportation = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b0a");
    public static readonly Guid Utilities = new("7d3c9f2a-6b1e-4f08-a5c4-2e9b0d1c7b0b");

    /// <summary>New instances every time (safe to add to a context), by name, with the colours the app always gave them.</summary>
    public static List<Category> Create() =>
    [
        new() { Id = Dining, Name = "Dining", Color = "#1F76B5" },
        new() { Id = Entertainment, Name = "Entertainment", Color = "#FF7E0F" },
        new() { Id = Groceries, Name = "Groceries", Color = "#2CA12D" },
        new() { Id = Health, Name = "Health", Color = "#D72628" },
        new() { Id = Other, Name = "Other", Color = "#9466BC" },
        new() { Id = Pet, Name = "Pet", Color = "#8C564B" },
        new() { Id = Rent, Name = "Rent", Color = "#E277C3" },
        new() { Id = Shopping, Name = "Shopping", Color = "#7F7E7E" },
        new() { Id = Subscriptions, Name = "Subscriptions", Color = "#BDBD23" },
        new() { Id = Transportation, Name = "Transportation", Color = "#16BFCF" },
        new() { Id = Utilities, Name = "Utilities", Color = "#800080" },
    ];

    /// <summary>The default category first named <paramref name="name"/> (ignoring case), whatever it is called now; null for any other name.</summary>
    public static Guid? IdOf(string name) =>
        Create().FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
}

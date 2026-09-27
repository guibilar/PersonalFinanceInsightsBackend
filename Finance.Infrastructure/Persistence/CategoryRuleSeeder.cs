using Finance.Application.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Finance.Infrastructure.Persistence;

/// <summary>
/// Copies the category rules from import-rules.json into the database the first time the API starts on it.
/// From then on the rules are edited in the app; the file is only the initial set.
/// </summary>
public static class CategoryRuleSeeder
{
    public static async Task SeedCategoryRulesAsync(this IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        if (await db.SeedMarkers.AnyAsync(m => m.Name == SeedMarker.CategoryRules))
        {
            return;
        }

        var rules = scope.ServiceProvider.GetRequiredService<IOptions<ImportOptions>>().Value.ToCategoryRules();
        db.CategoryRules.AddRange(rules);
        // Saved together with the rules: either both are stored or neither.
        db.SeedMarkers.Add(new SeedMarker { Name = SeedMarker.CategoryRules, SeededUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        logger.LogInformation("Stored the {Count} initial category rules from import-rules.json", rules.Count);
    }
}

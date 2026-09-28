using Finance.Application.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Finance.Infrastructure.Persistence;

/// <summary>
/// Copies the category and flow rules from import-rules.json into the database the first time the API starts on it
/// (each set once, so a database from before flow rules existed still gets them). From then on the rules are edited
/// in the app; the file is only the initial set.
/// </summary>
public static class RuleSeeder
{
    public static async Task SeedRulesAsync(this IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ImportOptions>>().Value;
        var seeded = await db.SeedMarkers.Select(m => m.Name).ToListAsync();

        if (!seeded.Contains(SeedMarker.CategoryRules))
        {
            var rules = options.ToCategoryRules();
            db.CategoryRules.AddRange(rules);
            // Saved together with the rules: either both are stored or neither.
            db.SeedMarkers.Add(new SeedMarker { Name = SeedMarker.CategoryRules, SeededUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            logger.LogInformation("Stored the {Count} initial category rules from import-rules.json", rules.Count);
        }

        if (!seeded.Contains(SeedMarker.FlowRules))
        {
            var rules = options.ToFlowRules();
            db.FlowRules.AddRange(rules);
            db.SeedMarkers.Add(new SeedMarker { Name = SeedMarker.FlowRules, SeededUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            logger.LogInformation("Stored the {Count} initial flow rules from import-rules.json", rules.Count);
        }
    }
}

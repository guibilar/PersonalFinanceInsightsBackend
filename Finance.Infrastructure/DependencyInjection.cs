using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Infrastructure.Import;
using Finance.Infrastructure.Persistence;
using Finance.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Finance.Infrastructure;

public static class DependencyInjection
{
    /// <summary>File with the bank import rules, copied to the output of every app that references this project.</summary>
    public const string ImportRulesFile = "import-rules.json";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(GetConnectionString(configuration), ConfigureNpgsql));

        services.AddOptions<ImportOptions>()
            .Bind(configuration.GetSection(ImportOptions.SectionName))
            .Validate(options => options.MonthlyRent?.IsValid() ?? true,
                "Import:MonthlyRent:Changes needs a \"From\" month as yyyy-MM and an amount above 0 in every entry.")
            .ValidateOnStart();

        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<ICategoryRuleRepository, CategoryRuleRepository>();
        services.AddScoped<IStatementParser, CibcCsvParser>();
        return services;
    }

    /// <summary>PostgreSQL settings, shared with the tests so they run against the same configuration.</summary>
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsqlOptions)
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    }

    private static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection is not set. Outside Development, provide it through the " +
            "ConnectionStrings__DefaultConnection environment variable.");

    /// <summary>Applies pending EF Core migrations; enabled with Database:MigrateOnStartup (e.g. in Docker).</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.MigrateAsync();
    }
}

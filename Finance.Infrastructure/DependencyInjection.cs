using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Infrastructure.Import;
using Finance.Infrastructure.Persistence;
using Finance.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Infrastructure;

public static class DependencyInjection
{
    /// <summary>File with the bank import rules, copied to the output of every app that references this project.</summary>
    public const string ImportRulesFile = "import-rules.json";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"), npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            }));

        services.Configure<ImportOptions>(configuration.GetSection(ImportOptions.SectionName));

        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IStatementParser, CibcCsvParser>();
        return services;
    }
}

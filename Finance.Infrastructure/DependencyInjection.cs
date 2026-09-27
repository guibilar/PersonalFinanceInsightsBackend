using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Finance.Application.Abstractions;
using Finance.Infrastructure.Import;
using Finance.Infrastructure.Persistence;
using Finance.Infrastructure.Repositories;

namespace Finance.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            }));

        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<ITransactionFileReader, CsvTransactionFileReader>();
        return services;
    }
}

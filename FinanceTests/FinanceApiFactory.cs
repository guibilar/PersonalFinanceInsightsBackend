using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Finance.Infrastructure;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace FinanceTests;

/// <summary>
/// Runs the real API against a private database: in-memory by default, or a freshly migrated PostgreSQL
/// database when FINANCE_TEST_POSTGRES holds a server connection string (as in CI).
/// Requests count as signed in (see <see cref="TestSignIn"/>) unless <paramref name="signedIn"/> is false,
/// which leaves the real cookie sign-in in charge.
/// </summary>
public class FinanceApiFactory(
    Action<IServiceCollection>? configureServices = null,
    bool signedIn = true,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const string PostgresVariable = "FINANCE_TEST_POSTGRES";

    private static readonly string? PostgresServer = Environment.GetEnvironmentVariable(PostgresVariable);
    private readonly string _databaseName = $"finance_test_{Guid.NewGuid():N}";
    private bool _createdDatabase;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }
        if (PostgresServer is not null)
        {
            // Migrated by the app itself, as in Docker, so startup work (the initial account) sees the tables.
            builder.UseSetting("Database:MigrateOnStartup", "true");
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FinanceDbContext>>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<FinanceDbContext>));
            services.AddDbContext<FinanceDbContext>(options =>
            {
                if (PostgresServer is null)
                {
                    options.UseInMemoryDatabase(_databaseName);
                }
                else
                {
                    options.UseNpgsql(new NpgsqlConnectionStringBuilder(PostgresServer) { Database = _databaseName }.ConnectionString,
                        DependencyInjection.ConfigureNpgsql);
                }
            });
            if (signedIn)
            {
                TestSignIn.Register(services);
            }
            configureServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        _createdDatabase = PostgresServer is not null;
        return host;
    }

    // Dispose() also ends up here; the database is dropped before the base class disposes the services.
    public override async ValueTask DisposeAsync()
    {
        if (_createdDatabase)
        {
            _createdDatabase = false;
            using var scope = Services.CreateScope();
            // ConfigureAwait(false): never resume on xUnit's context, so even a synchronous Dispose() of
            // the factory cannot deadlock on this cleanup.
            await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().Database.EnsureDeletedAsync()
                .ConfigureAwait(false);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }

    public async Task SeedAsync(Action<FinanceDbContext> seed)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> QueryAsync<T>(Func<FinanceDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinanceDbContext>());
    }
}

public static class HttpResponseExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(FinanceApiFactory.Json))!;
}

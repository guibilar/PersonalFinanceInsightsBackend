using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure;
using Finance.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace FinanceTests;

/// <summary>
/// Runs the real API against a private database: in-memory by default, or a freshly migrated PostgreSQL
/// database on the server <see cref="TestDatabase"/> picked for the run.
/// Requests count as signed in (see <see cref="TestSignIn"/>) unless <paramref name="signedIn"/> is false,
/// which leaves the real cookie sign-in in charge. With FINANCE_TEST_AUTH=gateway, signed-in requests use the auth
/// mode Gateway instead: each client sends a gateway token, as Hestia.Api does (see <see cref="TestGateway"/>).
/// </summary>
public class FinanceApiFactory(
    Action<IServiceCollection>? configureServices = null,
    bool signedIn = true,
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    private static string? PostgresServer => TestDatabase.PostgresServer;
    private readonly string _databaseName = $"finance_test_{Guid.NewGuid():N}";
    // Shared by the app's contexts and the one that creates the in-memory database before the app starts.
    private readonly InMemoryDatabaseRoot _inMemoryRoot = new();
    private bool _createdDatabase;

    /// <summary>This factory's own database on the PostgreSQL server; null on the in-memory provider.</summary>
    public string? ConnectionString => PostgresServer is null
        ? null
        : new NpgsqlConnectionStringBuilder(PostgresServer) { Database = _databaseName }.ConnectionString;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private bool GatewaySignIn => signedIn && TestGateway.AllTests;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (GatewaySignIn)
        {
            foreach (var (key, value) in TestGateway.Settings())
            {
                builder.UseSetting(key, value);
            }
        }
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
                    options.UseInMemoryDatabase(_databaseName, _inMemoryRoot);
                }
                else
                {
                    options.UseNpgsql(ConnectionString!, DependencyInjection.ConfigureNpgsql);
                }
            });
            if (GatewaySignIn)
            {
                // The requests come from the hestia-api container (the gateway tokens are accepted only from there).
                services.AddSingleton<IStartupFilter>(new FromPeer(TestGateway.Host));
            }
            else if (signedIn)
            {
                TestSignIn.Register(services);
            }
            configureServices?.Invoke(services);
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        if (GatewaySignIn)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TestGateway.Token(lifetime: TimeSpan.FromHours(1)));
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        if (PostgresServer is null)
        {
            // What the migrations insert on PostgreSQL (the default accounts and categories), in place before the app
            // starts, as its startup work (the rules it seeds) relies on them. A second host on the same database
            // finds it created already.
            using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(_databaseName, _inMemoryRoot).Options);
            db.Database.EnsureCreated();
        }
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

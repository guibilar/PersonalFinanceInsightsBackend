using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(FinanceTests.TestDatabase))]

namespace FinanceTests;

/// <summary>
/// Picks the database server for the whole test run from FINANCE_TEST_POSTGRES:
/// unset — EF Core's in-memory provider (fast, the default);
/// "container" — a PostgreSQL started in Docker with Testcontainers, removed after the run;
/// anything else — the connection string of a PostgreSQL server you run yourself.
/// Each test factory still gets its own database on the server.
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    public const string Variable = "FINANCE_TEST_POSTGRES";
    public const string ContainerSetting = "container";

    // Same major version as docker-compose.yml.
    private const string Image = "postgres:15-alpine";

    private PostgreSqlContainer? _container;

    /// <summary>PostgreSQL server connection string, or null for the in-memory provider.</summary>
    public static string? PostgresServer { get; private set; }

    public static bool OnPostgres => PostgresServer is not null;

    public async ValueTask InitializeAsync()
    {
        var setting = Environment.GetEnvironmentVariable(Variable);
        if (string.Equals(setting, ContainerSetting, StringComparison.OrdinalIgnoreCase))
        {
            _container = new PostgreSqlBuilder(Image).Build();
            await _container.StartAsync();
            PostgresServer = _container.GetConnectionString();
        }
        else
        {
            PostgresServer = string.IsNullOrWhiteSpace(setting) ? null : setting;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

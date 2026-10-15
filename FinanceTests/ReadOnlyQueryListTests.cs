using FinanceApi.Auth;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceTests;

/// <summary>
/// T44: the read-only POST queries of finance (<see cref="ReadOnlyQueryAttribute"/>) are listed in
/// <c>docs/read-only-queries.txt</c>. Hestia.Api's CI compares its <c>FinanceProxy:ReadOnlyQueries</c> with this file,
/// so the two lists cannot drift apart. This test fails when the file is not current: run the tests with
/// <c>FINANCE_UPDATE_READ_ONLY_QUERIES=1</c> and commit the file.
/// </summary>
public class ReadOnlyQueryListTests
{
    public const string UpdateVariable = "FINANCE_UPDATE_READ_ONLY_QUERIES";
    public const string RelativePath = "docs/read-only-queries.txt";

    [Fact]
    public async Task TheCommittedList_IsCurrent()
    {
        await using var factory = new FinanceApiFactory();
        factory.CreateClient();
        var paths = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ReadOnlyQueryAttribute>() is not null)
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(paths);
        var current = string.Join("\n", paths) + "\n";

        var path = Path.Combine(RepositoryRoot(), RelativePath);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, current);
            return;
        }
        var committed = File.Exists(path) ? (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n") : "";
        Assert.True(committed == current,
            $"{RelativePath} is not current. Run `{UpdateVariable}=1 dotnet test` and commit the file. Current list:\n{current}");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PersonalFinances.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("PersonalFinances.sln not found above the test folder.");
    }
}

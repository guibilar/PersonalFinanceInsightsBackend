using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace FinanceTests;

public class TransactionSortTests : IAsyncLifetime
{
    private readonly FinanceApiFactory _factory = new();
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        Transaction Tx(string description, decimal amount, int day, Category category) => new()
        {
            Id = Guid.NewGuid(), Description = description, Amount = amount, Category = category,
            Date = new DateTime(2026, 3, day, 0, 0, 0, DateTimeKind.Utc), Hash = Guid.NewGuid().ToString(),
            Account = AccountType.CreditCard, TransactionType = TransactionType.Debit
        };
        await _factory.SeedAsync(db => db.Transactions.AddRange(
            Tx("Bravo", 20, 2, Category.Shopping),
            Tx("alpha", 5, 3, Category.Dining),
            Tx("Charlie", 50, 1, Category.Groceries),
            Tx("Delta", 20, 4, Category.Utilities)));
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<List<string>> Descriptions(object filter)
    {
        var response = await _client.PostAsJsonAsync("/api/Transaction/Filter", filter);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadAsync<PaginatedList<TransactionOutputDto>>()).Items.Select(t => t.Description).ToList();
    }

    [Fact]
    public async Task DefaultsToNewestFirst()
    {
        Assert.Equal(["Delta", "alpha", "Bravo", "Charlie"], await Descriptions(new { }));
    }

    [Fact]
    public async Task SortsByAmount_TiesNewestFirst()
    {
        Assert.Equal(["Charlie", "Delta", "Bravo", "alpha"], await Descriptions(new { sortBy = "Amount" }));
        Assert.Equal(["alpha", "Delta", "Bravo", "Charlie"], await Descriptions(new { sortBy = "Amount", sortDirection = "Ascending" }));
    }

    [Fact]
    public async Task SortsByCategoryName()
    {
        Assert.Equal(["alpha", "Charlie", "Bravo", "Delta"], await Descriptions(new { sortBy = "Category", sortDirection = "Ascending" }));
    }

    [Fact]
    public async Task SortsByDateAscending()
    {
        Assert.Equal(["Charlie", "Bravo", "alpha", "Delta"], await Descriptions(new { sortBy = "Date", sortDirection = "Ascending" }));
    }

    [Fact]
    public async Task SortedPagesDoNotOverlap()
    {
        var first = await Descriptions(new { sortBy = "Amount", pageSize = 2, page = 1 });
        var second = await Descriptions(new { sortBy = "Amount", pageSize = 2, page = 2 });

        Assert.Empty(first.Intersect(second));
        Assert.Equal(4, first.Concat(second).Count());
    }

    [Fact]
    public async Task UnknownSortField_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/Transaction/Filter", new { sortBy = "Colour" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace FinanceTests;

/// <summary>Notes and tags on transactions: setting them, finding by them, totalling them, and the tags themselves.</summary>
public class TagTests
{
    private static Transaction Tx(string description, decimal amount, int day = 1) => new()
    {
        Id = Guid.NewGuid(), Date = new DateTime(2026, 9, day, 0, 0, 0, DateTimeKind.Utc), Description = description, Amount = amount,
        Flow = Flow.Spending, TransactionType = TransactionType.Debit, CategoryId = DefaultCategories.Dining,
        AccountId = Account.DefaultCreditCardId, Hash = Guid.NewGuid().ToString()
    };

    private static async Task<TagOutputDto> AddTagAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/Tags", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<TagOutputDto>();
    }

    private static Task<HttpResponseMessage> UpdateAsync(HttpClient client, Transaction transaction, object? note, object? tagIds) =>
        client.PutAsJsonAsync($"/api/Transaction/{transaction.Id}",
            new { description = transaction.Description, categoryId = transaction.CategoryId, note, tagIds });

    private static async Task<List<TransactionOutputDto>> FilterAsync(HttpClient client, object filter) =>
        (await (await client.PostAsJsonAsync("/api/Transaction/Filter", filter)).ReadAsync<PaginatedList<TransactionOutputDto>>()).Items;

    [Fact]
    public async Task Tags_AreNamedOnce_IgnoringCase_AndRenamed()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var trip = await AddTagAsync(client, "  trip   portugal ");
        Assert.Equal("trip portugal", trip.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/Tags", new { name = "Trip Portugal" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/Tags", new { name = new string('x', 41) })).StatusCode);

        var renamed = await (await client.PutAsJsonAsync($"/api/Tags/{trip.Id}", new { name = "Portugal 2026" })).ReadAsync<TagOutputDto>();
        Assert.Equal("Portugal 2026", renamed.Name);
        Assert.Equal(["Portugal 2026"], (await (await client.GetAsync("/api/Tags")).ReadAsync<List<TagOutputDto>>()).Select(t => t.Name));
    }

    [Fact]
    public async Task AnEdit_SetsTheNoteAndTags_OmittedKeepsThem_EmptyClearsThem()
    {
        await using var factory = new FinanceApiFactory();
        var gift = Tx("AMAZON.CA", 40);
        await factory.SeedAsync(db => db.Transactions.Add(gift));
        var client = factory.CreateClient();
        var family = await AddTagAsync(client, "family");
        var birthday = await AddTagAsync(client, "birthday");

        var set = await (await UpdateAsync(client, gift, "  Gift for mom ", new[] { family.Id, birthday.Id })).ReadAsync<TransactionOutputDto>();
        Assert.Equal("Gift for mom", set.Note);
        Assert.Equal(["birthday", "family"], set.Tags.Select(t => t.Name));

        var kept = await (await UpdateAsync(client, gift, null, null)).ReadAsync<TransactionOutputDto>();
        Assert.Equal(("Gift for mom", 2), (kept.Note, kept.Tags.Count));

        var cleared = await (await UpdateAsync(client, gift, "", Array.Empty<Guid>())).ReadAsync<TransactionOutputDto>();
        Assert.Equal((null, 0), (cleared.Note, cleared.Tags.Count));

        Assert.Equal(HttpStatusCode.BadRequest, (await UpdateAsync(client, gift, null, new[] { Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task TheList_FindsByTag_AndSearchesTheNoteToo()
    {
        await using var factory = new FinanceApiFactory();
        var hotel = Tx("HOTEL LISBOA", 300, 2);
        var dinner = Tx("RESTAURANTE", 60, 3);
        var coffee = Tx("STARBUCKS", 5, 4);
        await factory.SeedAsync(db => db.Transactions.AddRange(hotel, dinner, coffee));
        var client = factory.CreateClient();
        var trip = await AddTagAsync(client, "trip");
        await UpdateAsync(client, hotel, "Two nights", new[] { trip.Id });
        await UpdateAsync(client, dinner, "Anniversary dinner", new[] { trip.Id });

        Assert.Equal(["RESTAURANTE", "HOTEL LISBOA"], (await FilterAsync(client, new { tagId = trip.Id, pageSize = 50 })).Select(t => t.Description));
        Assert.Equal(["RESTAURANTE"], (await FilterAsync(client, new { description = "anniversary", pageSize = 50 })).Select(t => t.Description));

        var breakdown = await (await client.PostAsJsonAsync("/api/Reports/breakdown", new { tagId = trip.Id, interval = "Month" }))
            .ReadAsync<TransactionBreakdown>();
        Assert.Equal((2, 360m), (breakdown.Count, breakdown.Spending));
        Assert.Equal(2, (await (await client.GetAsync("/api/Tags")).ReadAsync<List<TagOutputDto>>()).Single().TransactionCount);
    }

    [Fact]
    public async Task ABulkChange_AddsAndTakesOffATag()
    {
        await using var factory = new FinanceApiFactory();
        var one = Tx("ONE", 1);
        var two = Tx("TWO", 2);
        await factory.SeedAsync(db => db.Transactions.AddRange(one, two));
        var client = factory.CreateClient();
        var work = await AddTagAsync(client, "work");
        var ids = new[] { one.Id, two.Id };

        var added = await (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = ids, addTagId = work.Id })).ReadAsync<BulkUpdateResultDto>();
        Assert.Equal(2, added.Updated);
        // Already there: nothing changes.
        Assert.Equal(0, (await (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = ids, addTagId = work.Id })).ReadAsync<BulkUpdateResultDto>()).Updated);
        Assert.Equal(2, (await FilterAsync(client, new { tagId = work.Id })).Count);

        await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = new[] { one.Id }, removeTagId = work.Id });
        Assert.Equal(["TWO"], (await FilterAsync(client, new { tagId = work.Id })).Select(t => t.Description));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/Transaction/Bulk", new { transactionIds = ids, addTagId = work.Id, removeTagId = work.Id })).StatusCode);
    }

    [Fact]
    public async Task DeletingATag_TakesItOffItsTransactions_AndKeepsThem()
    {
        await using var factory = new FinanceApiFactory();
        var one = Tx("ONE", 1);
        await factory.SeedAsync(db => db.Transactions.Add(one));
        var client = factory.CreateClient();
        var temp = await AddTagAsync(client, "temp");
        await UpdateAsync(client, one, null, new[] { temp.Id });

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/Tags/{temp.Id}")).StatusCode);

        var listed = Assert.Single(await FilterAsync(client, new { }));
        Assert.Empty(listed.Tags);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/Tags/{temp.Id}")).StatusCode);
    }

    [Fact]
    public async Task ATransactionAddedByHand_CanHaveANote()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Transaction", new
        {
            date = "2026-09-10", description = "Farmers market", amount = 12.5m, transactionType = "Debit",
            accountId = Account.DefaultCreditCardId, note = " Cash, honey and bread ",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Cash, honey and bread", (await response.ReadAsync<TransactionOutputDto>()).Note);
    }
}

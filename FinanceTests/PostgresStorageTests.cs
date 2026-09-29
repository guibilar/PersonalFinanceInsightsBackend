using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinanceTests;

/// <summary>Checks that only mean something on PostgreSQL; skipped on the in-memory provider.</summary>
public class PostgresStorageTests
{
    private static bool OnPostgres => TestDatabase.OnPostgres;

    [Fact]
    public async Task Enums_AreStoredByName()
    {
        Assert.SkipUnless(OnPostgres, "Needs PostgreSQL: set FINANCE_TEST_POSTGRES (e.g. to \"container\").");
        await using var factory = new FinanceApiFactory();
        _ = factory.CreateClient();
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = Guid.NewGuid(), Name = "Food", Description = "d", Amount = 1, Status = Status.Inactive });
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(), Date = DateTime.UtcNow, Description = "Coffee", Amount = 2, Hash = "h",
                CategoryId = DefaultCategories.Dining, TransactionType = TransactionType.Debit, AccountId = Account.DefaultCreditCardId
            });
        });

        var stored = await factory.QueryAsync(db => db.Database
            .SqlQueryRaw<string>("""SELECT "TransactionType" || ',' || "Flow" AS "Value" FROM "Transactions" """)
            .SingleAsync());
        var status = await factory.QueryAsync(db => db.Database
            .SqlQueryRaw<string>("""SELECT "Status" AS "Value" FROM "Budgets" """).SingleAsync());

        Assert.Equal("Debit,Spending", stored);
        Assert.Equal("Inactive", status);
    }
}

using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinanceTests;

/// <summary>Checks that only mean something on PostgreSQL; they pass trivially in in-memory mode.</summary>
public class PostgresStorageTests
{
    private static bool OnPostgres => Environment.GetEnvironmentVariable(FinanceApiFactory.PostgresVariable) is not null;

    [Fact]
    public async Task Enums_AreStoredByName()
    {
        if (!OnPostgres) return;
        await using var factory = new FinanceApiFactory();
        _ = factory.CreateClient();
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = Guid.NewGuid(), Name = "Food", Description = "d", Amount = 1, Status = Status.Inactive });
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(), Date = DateTime.UtcNow, Description = "Coffee", Amount = 2, Hash = "h",
                Category = Category.Dining, TransactionType = TransactionType.Debit, Account = AccountType.CreditCard
            });
        });

        var stored = await factory.QueryAsync(db => db.Database
            .SqlQueryRaw<string>("""SELECT "Category" || ',' || "TransactionType" || ',' || "Account" AS "Value" FROM "Transactions" """)
            .SingleAsync());
        var status = await factory.QueryAsync(db => db.Database
            .SqlQueryRaw<string>("""SELECT "Status" AS "Value" FROM "Budgets" """).SingleAsync());

        Assert.Equal("Dining,Debit,CreditCard", stored);
        Assert.Equal("Inactive", status);
    }
}

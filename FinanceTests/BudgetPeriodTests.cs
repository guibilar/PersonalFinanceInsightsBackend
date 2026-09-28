using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

public class BudgetPeriodTests
{
    private static bool OnPostgres => TestDatabase.OnPostgres;

    // 02:00 UTC on April 1st is still March 31st in Toronto, so "this month" is March.
    private static FinanceApiFactory FactoryOnMarch31() => new(services =>
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-04-01T02:00:00Z")))));

    private static void SeedSpending(FinanceDbContext db, Guid budgetId, BudgetPeriod period)
    {
        db.Budgets.Add(new Budget { Id = budgetId, Name = period.ToString(), Description = "d", Amount = 100, Status = Status.Active, Period = period });
        foreach (var (day, amount) in new[] { (new DateTime(2026, 2, 27), 50m), (new DateTime(2026, 3, 1), 20m), (new DateTime(2026, 3, 31), 5m) })
        {
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(), Date = DateTime.SpecifyKind(day, DateTimeKind.Utc), Description = "SHOP", Amount = amount,
                TransactionType = TransactionType.Debit, AccountId = Account.DefaultCreditCardId, Hash = Guid.NewGuid().ToString(), BudgetId = budgetId
            });
        }
    }

    [Fact]
    public async Task MonthlyBudget_CountsOnlyThisMonth_TotalBudgetCountsEverything()
    {
        await using var factory = FactoryOnMarch31();
        var monthly = Guid.NewGuid();
        var total = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            SeedSpending(db, monthly, BudgetPeriod.Monthly);
            SeedSpending(db, total, BudgetPeriod.Total);
        });
        var client = factory.CreateClient();

        var monthlyBudget = await (await client.GetAsync($"/api/Budget/{monthly}")).ReadAsync<BudgetOutputDto>();
        var page = await (await client.PostAsJsonAsync("/api/Budget/Filter", new { })).ReadAsync<PaginatedList<BudgetOutputDto>>();

        Assert.Equal((BudgetPeriod.Monthly, 25m, 75m), (monthlyBudget.Period, monthlyBudget.TotalSpent, monthlyBudget.TotalRemaining));
        Assert.Equal(25m, page.Items.Single(b => b.Id == monthly).TotalSpent);
        Assert.Equal(75m, page.Items.Single(b => b.Id == total).TotalSpent);
    }

    [Fact]
    public async Task Period_DefaultsToTotal_AndCanBeChanged()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/Budget",
            new { name = "Food", description = "d", status = "Active", amount = 400 })).ReadAsync<BudgetOutputDto>();
        var updated = await (await client.PutAsJsonAsync($"/api/Budget/{created.Id}",
            new { name = "Food", description = "d", status = "Active", amount = 400, period = "Monthly" })).ReadAsync<BudgetOutputDto>();
        var invalid = await client.PostAsJsonAsync("/api/Budget",
            new { name = "Food", description = "d", status = "Active", amount = 400, period = "Weekly" });

        Assert.Equal(BudgetPeriod.Total, created.Period);
        Assert.Equal(BudgetPeriod.Monthly, updated.Period);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Migration_ExistingBudgetsBecomeTotal()
    {
        Assert.SkipUnless(OnPostgres, "Needs PostgreSQL: set FINANCE_TEST_POSTGRES (e.g. to \"container\").");
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();
        var budgetId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("IndexTransactionDate");
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "Budgets" ("Id", "Name", "Description", "Status", "Amount") VALUES ({0}, 'Old', 'd', 'Active', 100)""", budgetId);
            await migrator.MigrateAsync();
        }

        var budget = await (await client.GetAsync($"/api/Budget/{budgetId}")).ReadAsync<BudgetOutputDto>();

        Assert.Equal(BudgetPeriod.Total, budget.Period);
    }
}

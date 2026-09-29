using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace FinanceTests;

/// <summary>Error format, status codes and response shapes introduced with the API modernisation.</summary>
public class ApiBehaviourTests
{
    private static Transaction NewTransaction(string description, decimal amount, DateTime date, Guid? budgetId = null) => new()
    {
        Id = Guid.NewGuid(),
        Description = description,
        Amount = amount,
        Date = DateTime.SpecifyKind(date, DateTimeKind.Utc),
        Category = Category.Dining,
        TransactionType = TransactionType.Debit,
        AccountId = Account.DefaultCreditCardId,
        Hash = Guid.NewGuid().ToString(),
        BudgetId = budgetId
    };

    [Fact]
    public async Task NotFound_IsReturnedAsProblemDetails()
    {
        await using var factory = new FinanceApiFactory();
        var id = Guid.NewGuid();

        var response = await factory.CreateClient().GetAsync($"/api/Budget/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Equal(404, problem.Status);
        Assert.Contains(id.ToString(), problem.Detail);
    }

    [Fact]
    public async Task UnexpectedErrors_Return500WithoutInternalDetails()
    {
        await using var factory = new FinanceApiFactory(services =>
            services.Replace(ServiceDescriptor.Scoped<IStatementParser, ThrowingParser>()));
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent("x"u8.ToArray()), "file", "statement.csv" },
            { new StringContent(Account.DefaultChequingId.ToString()), "accountId" }
        };

        var response = await factory.CreateClient().PostAsync("/api/Transaction/Import", form);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-path", body);
        Assert.Equal(500, (await response.ReadAsync<ProblemDetails>()).Status);
    }

    [Fact]
    public async Task CreateBudget_Returns201WithLocation()
    {
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/Budget",
            new { name = " Food ", description = "Groceries", status = "Active", amount = 100 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<BudgetOutputDto>();
        Assert.Equal("Food", created.Name);
        var fetched = await (await client.GetAsync(response.Headers.Location)).ReadAsync<BudgetOutputDto>();
        Assert.Equal(created.Id, fetched.Id);
    }

    [Fact]
    public async Task BudgetFilter_TotalsAreSummedPerBudget()
    {
        await using var factory = new FinanceApiFactory();
        var food = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = food, Name = "Food", Description = "d", Amount = 200 });
            db.Budgets.Add(new Budget { Id = Guid.NewGuid(), Name = "Travel", Description = "d", Amount = 50 });
            db.Transactions.Add(NewTransaction("A", 30, DateTime.UtcNow, food));
            db.Transactions.Add(NewTransaction("B", 20, DateTime.UtcNow, food));
            db.Transactions.Add(NewTransaction("C", 99, DateTime.UtcNow));
        });

        var page = await (await factory.CreateClient().PostAsJsonAsync("/api/Budget/Filter", new { }))
            .ReadAsync<PaginatedList<BudgetOutputDto>>();

        var foodBudget = page.Items.Single(b => b.Name == "Food");
        Assert.Equal(50, foodBudget.TotalSpent);
        Assert.Equal(25, foodBudget.PercentageUsed);
        Assert.Equal(0, page.Items.Single(b => b.Name == "Travel").TotalSpent);
    }

    [Fact]
    public async Task Transactions_ExposeLinkedBudgetAsReference_AndNullWhenUnlinked()
    {
        await using var factory = new FinanceApiFactory();
        var budgetId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Budgets.Add(new Budget { Id = budgetId, Name = "Food", Description = "d", Amount = 10 });
            db.Transactions.Add(NewTransaction("Linked", 5, DateTime.UtcNow, budgetId));
            db.Transactions.Add(NewTransaction("Unlinked", 5, DateTime.UtcNow.AddMinutes(-1)));
        });

        var page = await (await factory.CreateClient().PostAsJsonAsync("/api/Transaction/Filter", new { }))
            .ReadAsync<PaginatedList<TransactionOutputDto>>();

        var linked = page.Items.Single(t => t.Description == "Linked");
        Assert.Equal(budgetId, linked.BudgetId);
        Assert.Equal("Food", linked.Budget?.Name);
        var unlinked = page.Items.Single(t => t.Description == "Unlinked");
        Assert.Null(unlinked.BudgetId);
        Assert.Null(unlinked.Budget);
    }

    [Fact]
    public async Task Reports_CardRefundsReduceSpending_ChequingIncomeDoesNot()
    {
        var now = new DateTimeOffset(2026, 2, 15, 17, 0, 0, TimeSpan.Zero);
        await using var factory = new FinanceApiFactory(services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(now))));
        Transaction Tx(string description, decimal amount, TransactionType type, AccountType account, Category category) => new()
        {
            Id = Guid.NewGuid(), Description = description, Amount = amount, TransactionType = type,
            AccountId = TestAccounts.Id(account), Flow = TestAccounts.DefaultFlow(account, type),
            Category = category, Date = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), Hash = Guid.NewGuid().ToString()
        };
        await factory.SeedAsync(db => db.Transactions.AddRange(
            Tx("WAL-MART", 120, TransactionType.Debit, AccountType.CreditCard, Category.Groceries),
            Tx("WAL-MART", 20, TransactionType.Credit, AccountType.CreditCard, Category.Groceries),
            Tx("NETFLIX", 17, TransactionType.Debit, AccountType.CreditCard, Category.Subscriptions),
            Tx("Deel Canada PAYROLL", 4200, TransactionType.Credit, AccountType.Chequing, Category.Other)));

        var month = await (await factory.CreateClient().GetAsync("/api/Reports/current-month-categorized-graph"))
            .ReadAsync<MonthlyCategorizedGraphData>();

        var feb = Assert.Single(month.MonthlyData);
        Assert.Equal(117m, feb.Total);
        Assert.Equal(100m, feb.CategoryValues[Category.Groceries]);
        Assert.DoesNotContain(Category.Other, month.Categories);
    }

    [Fact]
    public async Task Reports_UseTheInjectedClock()
    {
        var now = new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);
        await using var factory = new FinanceApiFactory(services =>
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(now))));
        await factory.SeedAsync(db =>
        {
            db.Transactions.Add(NewTransaction("Last year", 10, new DateTime(2025, 12, 31, 23, 0, 0)));
            db.Transactions.Add(NewTransaction("January", 20, new DateTime(2026, 1, 5)));
            db.Transactions.Add(NewTransaction("February", 30, new DateTime(2026, 2, 28, 23, 59, 0)));
            db.Transactions.Add(NewTransaction("March", 40, new DateTime(2026, 3, 1)));
        });
        var client = factory.CreateClient();

        var month = await (await client.GetAsync("/api/Reports/current-month-categorized-graph")).ReadAsync<MonthlyCategorizedGraphData>();
        var year = await (await client.GetAsync("/api/Reports/monthly-categorized-graph")).ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal(("Feb 2026", 30m), (Assert.Single(month.MonthlyData).Month, month.MonthlyData[0].Total));
        Assert.Equal(["Jan 2026", "Feb 2026", "Mar 2026"], year.MonthlyData.Select(m => m.Month));
        // Only finished months count: February is still running and March hasn't started.
        Assert.Equal(20, year.AverageMonthlyTotal);
    }

    [Theory]
    // On March 5th: February 1-5. The refund on the 3rd gives money back; the 6th is past that point.
    [InlineData("2026-03-05T17:00:00Z", 1947.5 + 40 - 15)]
    // On March 30th: February has only 28 days, so all of it.
    [InlineData("2026-03-30T17:00:00Z", 1947.5 + 40 - 15 + 60)]
    // 01:30 UTC on March 1st is still February 28th in Toronto: January 1-28.
    [InlineData("2026-03-01T01:30:00Z", 100)]
    public async Task CurrentMonthReport_ComparesWithThePreviousMonthUpToTheSameDay(string utcNow, double expected)
    {
        await using var factory = FactoryAt(utcNow);
        await factory.SeedAsync(db =>
        {
            db.Transactions.Add(NewTransaction("January", 100, new DateTime(2026, 1, 20)));
            db.Transactions.Add(NewTransaction("Rent", 1947.5m, new DateTime(2026, 2, 1)));
            db.Transactions.Add(NewTransaction("Groceries", 40, new DateTime(2026, 2, 2)));
            var refund = NewTransaction("Refund", 15, new DateTime(2026, 2, 3));
            refund.TransactionType = TransactionType.Credit;
            refund.Flow = Flow.Refund;
            db.Transactions.Add(refund);
            db.Transactions.Add(NewTransaction("Later in February", 60, new DateTime(2026, 2, 6)));
            db.Transactions.Add(NewTransaction("March", 70, new DateTime(2026, 3, 2)));
        });

        var month = await (await factory.CreateClient().GetAsync("/api/Reports/current-month-categorized-graph"))
            .ReadAsync<MonthlyCategorizedGraphData>();
        var year = await (await factory.CreateClient().GetAsync("/api/Reports/monthly-categorized-graph"))
            .ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal((decimal)expected, month.PreviousMonthToDateTotal);
        Assert.Null(year.PreviousMonthToDateTotal);
    }

    [Fact]
    public async Task YearReport_InItsFirstMonth_AveragesThatMonth()
    {
        await using var factory = FactoryAt("2026-01-20T12:00:00Z");
        await factory.SeedAsync(db => db.Transactions.Add(NewTransaction("January", 20, new DateTime(2026, 1, 5))));

        var year = await (await factory.CreateClient().GetAsync("/api/Reports/monthly-categorized-graph"))
            .ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal(20, year.AverageMonthlyTotal);
    }

    private static FinanceApiFactory FactoryAt(string utcNow) => new(services =>
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse(utcNow)))));

    [Theory]
    // 01:30 UTC on March 1st is still the evening of February 28th in Toronto (appsettings.json).
    [InlineData("2026-03-01T01:30:00Z", "Feb 2026")]
    [InlineData("2026-03-01T05:30:00Z", "Mar 2026")]
    public async Task Reports_CurrentMonthFollowsTheConfiguredTimeZone(string utcNow, string expectedMonth)
    {
        await using var factory = FactoryAt(utcNow);
        await factory.SeedAsync(db =>
        {
            db.Transactions.Add(NewTransaction("February", 30, new DateTime(2026, 2, 28)));
            db.Transactions.Add(NewTransaction("March", 40, new DateTime(2026, 3, 1)));
        });

        var month = await (await factory.CreateClient().GetAsync("/api/Reports/current-month-categorized-graph"))
            .ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal(expectedMonth, Assert.Single(month.MonthlyData).Month);
    }

    [Fact]
    public async Task Reports_YearFollowsTheConfiguredTimeZone()
    {
        // 03:00 UTC on January 1st is still New Year's Eve in Toronto.
        await using var factory = FactoryAt("2027-01-01T03:00:00Z");
        await factory.SeedAsync(db => db.Transactions.Add(NewTransaction("December", 30, new DateTime(2026, 12, 31))));

        var year = await (await factory.CreateClient().GetAsync("/api/Reports/monthly-categorized-graph"))
            .ReadAsync<MonthlyCategorizedGraphData>();

        Assert.Equal("Dec 2026", Assert.Single(year.MonthlyData).Month);
    }

    [Fact]
    public async Task CancelledRequests_AreNotLoggedAsErrors()
    {
        var logs = new CapturingLoggerProvider();
        var reports = new BlockingReportService();
        await using var factory = new FinanceApiFactory(services =>
        {
            services.AddLogging(logging => logging
                .AddProvider(logs)
                .AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace));
            services.Replace(ServiceDescriptor.Scoped<IReportService>(_ => reports));
        });
        var client = factory.CreateClient();
        using var cancel = new CancellationTokenSource();

        var request = client.GetAsync("/api/Reports/current-month-categorized-graph", cancel.Token);
        await reports.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await reports.Cancelled.WaitAsync(TimeSpan.FromSeconds(10));

        // ASP.NET Core recognises the abort itself (at Debug level) and never calls the exception handler.
        bool Aborted() => logs.Entries.Any(e => e.Message == "The request was aborted by the client.");
        for (var waited = 0; !Aborted() && waited < 5000; waited += 20) await Task.Delay(20);
        Assert.True(Aborted());
        // Where an unhandled exception would be logged (the Postgres test setup logs unrelated EF errors).
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error &&
            (e.Category.StartsWith("Microsoft.AspNetCore") || e.Category.StartsWith("FinanceApi")));
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    public async Task Health_ReportsHealthyWhenTheDatabaseIsReachable(string path)
    {
        await using var factory = new FinanceApiFactory();

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_DatabaseOutage_FailsReadinessButNotLiveness()
    {
        await using var factory = new FinanceApiFactory(services =>
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                var database = options.Registrations.Single(r => r.Name == "database");
                database.Factory = _ => new UnreachableDatabase();
            }));
        var client = factory.CreateClient();

        var ready = await client.GetAsync("/health/ready");
        var legacy = await client.GetAsync("/health");
        var live = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, legacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    private class UnreachableDatabase : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Unhealthy("Cannot connect to the database."));
    }

    private class BlockingReportService : IReportService
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;
        public Task Cancelled => _cancelled.Task;

        public Task<MonthlyCategorizedGraphData> GenerateMonthlySpendCategorizedGraphDataAsync(CancellationToken cancellationToken = default) =>
            GenerateCurrentMonthCategorizedGraphDataAsync(cancellationToken);

        public async Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync(CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            }
            finally
            {
                _cancelled.TrySetResult();
            }
        }

        public Task<CashFlowReport> GenerateCashFlowReportAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<(string Category, LogLevel Level, string Message)> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }

        private class Logger(CapturingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                provider.Entries.Add((category, logLevel, formatter(state, exception)));
        }
    }

    private class ThrowingParser : IStatementParser
    {
        public StatementFormat Format => StatementFormat.Cibc;

        public ParsedStatement Parse(Stream statement) =>
            throw new InvalidDataException("Bad row in '/secret-path/cibc_credit.csv'");
    }
}

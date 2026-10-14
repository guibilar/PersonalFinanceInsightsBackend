using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Infrastructure.Events;
using FinanceApi.Health;
using Hestia.Contracts.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace FinanceTests;

/// <summary>
/// T37: finance starts without the broker, keeps its events in the outbox, reports <c>events-broker</c> as Degraded,
/// and sends the events when the broker comes back, without a restart. A <see cref="BrokerGate"/> in front of the test
/// broker plays the broker that is down. These tests need PostgreSQL and RabbitMQ (CI uses "container").
/// </summary>
public class BrokerTests
{
    private const string CountOutgoingEnvelopes =
        "SELECT count(*)::int AS \"Value\" FROM " + FinanceEventsOptions.OutboxSchema + ".wolverine_outgoing_envelopes";

    [Fact]
    public async Task WithoutEvents_TheCheckIsHealthy()
    {
        await using var factory = new FinanceApiFactory();
        factory.CreateClient();

        var result = await CheckAsync(factory);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("off", result.Description);
    }

    [Fact]
    public async Task WithTheBroker_TheCheckIsHealthy()
    {
        Assert.SkipUnless(TestDatabase.OnPostgres, $"Needs PostgreSQL: set {TestDatabase.Variable}.");
        Assert.SkipUnless(TestRabbitMq.Available, $"Needs RabbitMQ: set {TestRabbitMq.Variable}.");
        await using var factory = Start(TestRabbitMq.Address!, new LogLines());
        factory.CreateClient();

        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(factory)).Status);
    }

    [Fact]
    public async Task BrokerDownAtStart_FinanceWorks_AndSendsTheWaitingEventsWhenTheBrokerIsBack()
    {
        Assert.SkipUnless(TestDatabase.OnPostgres, $"Needs PostgreSQL: set {TestDatabase.Variable}.");
        Assert.SkipUnless(TestRabbitMq.Available, $"Needs RabbitMQ: set {TestRabbitMq.Variable}.");
        await using var gate = new BrokerGate(TestRabbitMq.Address!);
        var logs = new LogLines();
        await using var factory = Start(gate.Address, logs);
        var clock = Stopwatch.StartNew();

        var client = Phase("start", factory.CreateClient, logs);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"The start took {clock.Elapsed}. {logs}");
        Assert.Equal(HealthStatus.Degraded, (await CheckAsync(factory)).Status);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        // A change while the broker is down: it saves, and its event waits in the outbox.
        var description = $"BAKERY {Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/Transaction", new
        {
            date = "2026-10-12", description, amount = 7.25m, transactionType = "Debit",
            accountId = Account.DefaultCreditCardId, categoryId = DefaultCategories.Groceries
        }, FinanceApiFactory.Json);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"The change failed while the broker is down: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()} {logs}");
        var created = await response.ReadAsync<TransactionOutputDto>();
        Assert.True(await OutgoingAsync(factory) > 0, $"No envelope in the outbox. {logs}");

        // The broker comes back: the queue of the test binds first, so it sees the event.
        await using var events = await FinanceEventQueue.BindAsync(declareExchange: true);
        gate.Open();

        TransactionChanged sent;
        try
        {
            sent = await events.WaitForAsync<TransactionChanged>(e => e.TransactionId == created.Id, TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException ex)
        {
            Assert.Fail($"{ex.Message} Outbox: {await OutgoingAsync(factory)}. {logs}");
            throw;
        }
        Assert.Equal(description, sent.Description);
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(factory)).Status);
        Assert.Equal(0, await Eventually(() => OutgoingAsync(factory), count => count == 0));
        Assert.NotEmpty(logs.Find("connected to the RabbitMQ broker after the start"));

        // A change after the reconnect goes out at once.
        var later = await (await client.PostAsJsonAsync("/api/Transaction", new
        {
            date = "2026-10-12", description = description + " 2", amount = 3m, transactionType = "Debit",
            accountId = Account.DefaultCreditCardId, categoryId = DefaultCategories.Groceries
        }, FinanceApiFactory.Json)).ReadAsync<TransactionOutputDto>();
        await events.WaitForAsync<TransactionChanged>(e => e.TransactionId == later.Id);
    }

    /// <summary>Runs one step of a test, and names the step and the stack in the failure (CI shows only the message).</summary>
    private static T Phase<T>(string name, Func<T> step, LogLines logs)
    {
        try
        {
            return step();
        }
        catch (Exception ex)
        {
            // Only the method of each frame, so the message keeps the whole stack.
            var stack = string.Join(" < ", (ex.ToString()).Split('\n').Select(line => line.Trim())
                .Where(line => line.StartsWith("at ", StringComparison.Ordinal))
                .Select(line => line[3..].Split(" in ")[0].Split('(')[0].Replace("Wolverine.", "W.").Replace("Microsoft.", "M."))
                .Take(40));
            Assert.Fail($"The step '{name}' failed: {ex.GetType().Name}: {ex.Message[..Math.Min(80, ex.Message.Length)]} AT {stack}");
            throw;
        }
    }

    private static FinanceApiFactory Start(string rabbitMq, LogLines logs)
    {
        FinanceApiFactory? factory = null;
        factory = new FinanceApiFactory(services =>
        {
            services.AddFinanceEvents(new FinanceEventsOptions
            {
                RabbitMq = rabbitMq,
                Broker = new FinanceBrokerOptions
                {
                    StartTimeout = TimeSpan.Zero,
                    ReconnectInterval = TimeSpan.FromMilliseconds(500)
                }
            }, factory!.ConnectionString!);
            services.AddSingleton<ILoggerProvider>(logs);
        });
        return factory;
    }

    private static async Task<HealthReportEntry> CheckAsync(FinanceApiFactory factory)
    {
        var report = await factory.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == BrokerHealthCheck.Name);
        return report.Entries[BrokerHealthCheck.Name];
    }

    private static Task<int> OutgoingAsync(FinanceApiFactory factory) =>
        factory.QueryAsync(db => db.Database.SqlQueryRaw<int>(CountOutgoingEnvelopes).SingleAsync());

    private static async Task<T> Eventually<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var value = await read();
            if (done(value) || DateTimeOffset.UtcNow > deadline)
            {
                return value;
            }
            await Task.Delay(100);
        }
    }

    /// <summary>Keeps the log lines about the broker and the outbox, for a failure message (CI shows only the message).</summary>
    private sealed class LogLines : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Category, string Message)> _lines = new();

        public IReadOnlyList<string> Find(string text) =>
            _lines.Where(line => line.Message.Contains(text, StringComparison.Ordinal)).Select(line => line.Message).ToList();

        public override string ToString() => "Log: " + string.Join(" | ", _lines
            .Where(line => line.Level >= LogLevel.Information &&
                           (line.Category.Contains("Wolverine", StringComparison.Ordinal) || line.Category.Contains("Finance", StringComparison.Ordinal)) &&
                           !line.Message.StartsWith("Executed", StringComparison.Ordinal))
            .TakeLast(14)
            .Select(line => $"{line.Level} {line.Category.Split('.').Last()}: {(line.Message.Length > 180 ? line.Message[..180] : line.Message)}"));

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<(LogLevel, string, string)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                lines.Enqueue((logLevel, category, formatter(state, exception) + (exception is null ? "" : $" [{exception.GetBaseException().Message}]")));
        }
    }
}

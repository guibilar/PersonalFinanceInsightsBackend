using Finance.Application.Events;
using Finance.Infrastructure.Persistence;
using Hestia.Contracts.Finance;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.EntityFrameworkCore.Internals;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using Wolverine.RabbitMQ.Internal;

namespace Finance.Infrastructure.Events;

/// <summary>The <c>Events</c> settings of the finance service (T04).</summary>
public sealed class FinanceEventsOptions
{
    public const string SectionName = "Events";

    /// <summary>The database schema of the Wolverine outbox tables, next to the finance tables.</summary>
    public const string OutboxSchema = "wolverine";

    /// <summary>
    /// The RabbitMQ broker, for example <c>amqp://hestia:secret@rabbitmq:5672</c>. Empty: no events, and finance works as
    /// before (no outbox, no extra queries).
    /// </summary>
    public string? RabbitMq { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(RabbitMq);

    /// <summary>The start timeout of the broker connection (T37).</summary>
    public FinanceBrokerOptions Broker { get; set; } = new();

    public Uri RabbitMqUri()
    {
        if (!Uri.TryCreate(RabbitMq, UriKind.Absolute, out var uri) || uri.Scheme is not ("amqp" or "amqps"))
        {
            throw new InvalidOperationException(
                $"{SectionName}:RabbitMq must be an amqp:// or amqps:// address, for example amqp://user:password@rabbitmq:5672.");
        }
        return uri;
    }
}

public static class FinanceEventsSetup
{
    /// <summary>The durable local queue of the events on their way to the broker (T79).</summary>
    public const string ForwardQueue = "finance-events-forward";

    /// <summary>
    /// Turns the finance events on: the Wolverine outbox in the finance database (schema <c>wolverine</c>), and
    /// RabbitMQ. Every finance event goes to the fanout exchange <see cref="FinanceEventTopology.Exchange"/>.
    /// Call it only when <see cref="FinanceEventsOptions.Enabled"/> is true; without it, the services raise nothing.
    /// </summary>
    /// <param name="connectionString">The finance database: the outbox tables are in it, so they commit with the changes.</param>
    public static IServiceCollection AddFinanceEvents(this IServiceCollection services, FinanceEventsOptions options,
        string connectionString)
    {
        var rabbitMq = options.RabbitMqUri();
        if (!options.Broker.IsValid())
        {
            throw new InvalidOperationException(
                $"{FinanceEventsOptions.SectionName}:Broker:StartTimeout must be zero or more, and ReconnectInterval more than zero.");
        }

        services.Replace(ServiceDescriptor.Singleton(new FinanceEventsSwitch(Enabled: true)));
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<FinanceBrokerReconnector>();
        services.AddScoped<FinanceOutbox>();
        services.AddScoped<FinanceOutboxSaveInterceptor>();
        services.AddScoped<FinanceOutboxTransactionInterceptor>();
        services.AddFinanceOutboxOnDbContext();

        services.AddWolverine(wolverine =>
        {
            wolverine.ApplicationAssembly = typeof(FinanceEventsSetup).Assembly;
            // Finance only publishes: it has no message handlers.
            wolverine.Discovery.DisableConventionalDiscovery();
            // One finance process: no leader election between nodes.
            wolverine.Durability.Mode = DurabilityMode.Solo;
            // The outbox tables, in the finance database. Wolverine makes them when it starts.
            wolverine.PersistMessagesWithPostgresql(connectionString, FinanceEventsOptions.OutboxSchema);

            wolverine.UseRabbitMq(rabbitMq)
                .AutoProvision()
                // Finance only publishes: no request/reply system queue (a listener that finance does not use).
                .DisableSystemRequestReplyQueueDeclaration()
                .DeclareExchange(FinanceEventTopology.Exchange, exchange => exchange.ExchangeType = ExchangeType.Fanout);
            // Durable: the forward stores the event as an outgoing envelope before the local message counts as done,
            // and Wolverine sends it (also later, after a broker outage). The default for an exchange that no publish
            // rule names is inline, which retries a failed send in memory and then discards the event.
            wolverine.Transports.GetOrCreate<RabbitMqTransport>().Exchanges[FinanceEventTopology.Exchange].Mode = EndpointMode.Durable;
            // T79: each event goes first to a durable local queue (stored with the change, in the finance database),
            // and FinanceEventForwarder sends it to the exchange. Wolverine builds the routes of the published events
            // at its start; a route to RabbitMQ needs a broker connection, but a route to a local queue does not. So a
            // broker that is down at the start no longer stops it.
            wolverine.LocalQueue(ForwardQueue).UseDurableInbox();
            foreach (var eventType in FinanceEventTopology.EventTypes)
            {
                wolverine.PublishMessage(eventType).ToLocalQueue(ForwardQueue);
                wolverine.Discovery.IncludeType(typeof(FinanceEventForwarder<>).MakeGenericType(eventType));
            }
            wolverine.Policies.OnException(_ => true, "Finance event forward (T79)")
                .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))
                .Then.ScheduleRetryIndefinitely(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));

            // T37, T79: a broker that is down when finance starts does not stop the start. ContinueOnFailures skips
            // the transport; the routes of the events go to the local queue, which needs no broker; and
            // FinanceBrokerReconnector connects later. A broker that stops later is no problem: the RabbitMQ client
            // connects again, and the queue keeps the events meanwhile. The start waits at most
            // Events:Broker:StartTimeout for the broker.
            wolverine.ResourceMigrationFailureMode = ResourceMigrationFailureMode.ContinueOnFailures;
            wolverine.BrokerInitializationTimeout = options.Broker.StartTimeout;
        });
        return services;
    }

    /// <summary>
    /// Maps the Wolverine envelope tables into the <see cref="FinanceDbContext"/> at run time (not in the migrations:
    /// Wolverine owns those tables), and adds the outbox interceptors. Tests that replace the DbContext options call it
    /// again after their own registration.
    /// </summary>
    public static IServiceCollection AddFinanceOutboxOnDbContext(this IServiceCollection services) =>
        services.ConfigureDbContext<FinanceDbContext>((provider, options) => options
            .ReplaceService<IModelCustomizer, WolverineModelCustomizer>()
            .ReplaceService<IModelCacheKeyFactory, WolverineModelCacheKeyFactory>()
            .AddInterceptors(
                provider.GetRequiredService<FinanceOutboxSaveInterceptor>(),
                provider.GetRequiredService<FinanceOutboxTransactionInterceptor>()));

    /// <summary>Fails at start when the outbox is not mapped into the DbContext (a setup error).</summary>
    public static async Task EnsureFinanceOutboxAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        if (db.Model.FindEntityType(typeof(OutgoingMessage)) is null)
        {
            throw new InvalidOperationException(
                "The finance events are on, but the Wolverine outbox is not mapped into FinanceDbContext (AddFinanceOutboxOnDbContext).");
        }
    }
}

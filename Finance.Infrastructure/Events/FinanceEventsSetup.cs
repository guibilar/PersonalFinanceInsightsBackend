using Finance.Application.Events;
using Finance.Infrastructure.Persistence;
using Hestia.Contracts.Finance;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;
using Wolverine.EntityFrameworkCore.Internals;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

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

    /// <summary>The start timeout and the reconnect of the broker connection (T37).</summary>
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
        services.AddScoped<FinanceOutbox>();
        services.AddScoped<FinanceOutboxSaveInterceptor>();
        services.AddScoped<FinanceOutboxTransactionInterceptor>();
        services.AddFinanceOutboxOnDbContext();
        // T37: connects to the broker when it comes back after a start without it, and sends the waiting events.
        services.AddSingleton(options);
        services.AddHostedService<FinanceBrokerReconnector>();

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
                .DeclareExchange(FinanceEventTopology.Exchange, exchange => exchange.ExchangeType = ExchangeType.Fanout);
            foreach (var eventType in FinanceEventTopology.EventTypes)
            {
                // Durable: the envelope is stored with the change, and sent after the commit.
                wolverine.PublishMessage(eventType).ToRabbitExchange(FinanceEventTopology.Exchange).UseDurableOutbox();
            }

            // A broker that is down when finance starts must not stop finance. The events then wait in the outbox, and
            // FinanceBrokerReconnector sends them when the broker is back (T37). A short start timeout (Wolverine's
            // default is 2 minutes), so finance answers requests soon.
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

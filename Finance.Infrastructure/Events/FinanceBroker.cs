using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine.Persistence.Durability;
using Wolverine.RabbitMQ.Internal;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace Finance.Infrastructure.Events;

/// <summary>The connection to the RabbitMQ broker (<c>Events:Broker</c>, T37).</summary>
public sealed class FinanceBrokerOptions
{
    /// <summary>
    /// How long finance tries to reach the broker when it starts. After that, it starts without the broker, keeps the
    /// events in its outbox, and <see cref="FinanceBrokerReconnector"/> tries again in the background. Wolverine's own
    /// default is 2 minutes, and finance does not answer requests during that time.
    /// </summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often <see cref="FinanceBrokerReconnector"/> tries the broker again.</summary>
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(15);

    public bool IsValid() => StartTimeout >= TimeSpan.Zero && ReconnectInterval > TimeSpan.Zero;
}

/// <summary>The state of the RabbitMQ transport of finance.</summary>
public static class FinanceBroker
{
    /// <summary>The RabbitMQ transport, or null without the finance events.</summary>
    public static RabbitMqTransport? Transport(IWolverineRuntime runtime) =>
        runtime.Options.Transports.OfType<RabbitMqTransport>().FirstOrDefault();

    /// <summary>True when the transport has an open connection to the broker.</summary>
    public static async Task<bool> ConnectedAsync(IWolverineRuntime runtime, CancellationToken cancellationToken) =>
        Transport(runtime) is { } transport &&
        (await transport.ProbeAsync(cancellationToken)).Status is BrokerHealthStatus.Healthy or BrokerHealthStatus.Degraded;
}

/// <summary>
/// Connects the RabbitMQ transport when the broker was down at the start of finance (T37). Wolverine (with
/// <c>ContinueOnFailures</c>) then starts finance without the transport and does not try again: the events wait in the
/// outbox (<c>wolverine.wolverine_outgoing_envelopes</c>). Every <see cref="FinanceBrokerOptions.ReconnectInterval"/>,
/// this service tries the broker. When it answers, the service declares the exchange, starts the sender, and releases
/// the waiting envelopes, so Wolverine's durability agent sends them. No restart is necessary.
/// </summary>
/// <remarks>
/// A broker that goes down <i>after</i> a connection needs nothing from this service: the RabbitMQ client recovers the
/// connection itself, and the outbox keeps the events meanwhile.
/// </remarks>
public sealed class FinanceBrokerReconnector(
    IWolverineRuntime runtime,
    FinanceEventsOptions options,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<FinanceBrokerReconnector> logger) : BackgroundService
{
    private bool _reportedDown;
    private bool _needsSetup;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await HostStartedAsync(stoppingToken))
        {
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Events: the check of the RabbitMQ connection failed. It tries again in {Interval}",
                    options.Broker.ReconnectInterval);
            }

            try
            {
                await Task.Delay(options.Broker.ReconnectInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One check: connect when the transport never connected, then send the waiting events.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (FinanceBroker.Transport(runtime) is not { } transport)
        {
            return;
        }

        if ((await transport.ProbeAsync(cancellationToken)).Status == BrokerHealthStatus.Unknown)
        {
            try
            {
                // One quick try. It throws while the broker is down, and leaves the transport as it was.
                await transport.ConnectAsync(runtime);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (!_reportedDown)
                {
                    _reportedDown = true;
                    logger.LogWarning("Events: the RabbitMQ broker is not reachable ({Error}). The events wait in the " +
                        "outbox; finance tries again every {Interval}", ex.GetBaseException().Message, options.Broker.ReconnectInterval);
                }
                return;
            }
            _reportedDown = false;
            _needsSetup = true;
        }

        if (!_needsSetup)
        {
            return;
        }

        // Declares the exchange (AutoProvision), as a normal start does.
        await transport.InitializeAsync(runtime);
        foreach (var endpoint in transport.Endpoints().Where(endpoint => endpoint.AutoStartSendingAgent()))
        {
            runtime.Endpoints.GetOrBuildSendingAgent(endpoint.Uri);
        }
        // The envelopes of the outage still belong to this process, and the durability agent sends only free ones.
        // Free them: the agent sends them at its next run. One finance process (Solo), so nothing else owns them.
        await runtime.Storage.Admin.ReleaseAllOwnershipAsync();
        _needsSetup = false;
        logger.LogInformation("Events: connected to the RabbitMQ broker after the start. The events in the outbox go out now");
    }

    private async Task<bool> HostStartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await using var onStopping = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
        try
        {
            await started.Task;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

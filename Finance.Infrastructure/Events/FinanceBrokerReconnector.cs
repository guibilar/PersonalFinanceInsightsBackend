using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine.Runtime;

namespace Finance.Infrastructure.Events;

/// <summary>
/// Connects the RabbitMQ transport when the broker was down at the start of finance (T79). Wolverine (with
/// <c>ContinueOnFailures</c>) then starts without the connection and does not try again. Every
/// <see cref="FinanceBrokerOptions.ReconnectInterval"/>, until the first connection, this service connects the transport
/// and declares the exchange. The events wait in the durable local queue meanwhile; the forwarder sends them when the
/// connection is there.
/// A broker that goes down <i>after</i> a connection needs nothing from this service: the RabbitMQ client recovers the
/// connection itself.
/// </summary>
public sealed class FinanceBrokerReconnector(
    IWolverineRuntime runtime,
    FinanceEventsOptions options,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<FinanceBrokerReconnector> logger) : BackgroundService
{
    private bool _reportedDown;

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
                if (await CheckAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Events: the connect to the RabbitMQ broker failed. Finance tries again in {Interval}",
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

    /// <summary>One try. True when the transport has connected (now or before): the service then has no more work.</summary>
    internal async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        if (FinanceBroker.Transport(runtime) is not { } transport)
        {
            return true;
        }
        if (FinanceBroker.ConnectedOnce(await transport.ProbeAsync(cancellationToken)))
        {
            return true;
        }
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
                logger.LogWarning("Events: the RabbitMQ broker is not reachable ({Error}). The finance events wait in " +
                    "the outbox; finance tries again every {Interval}", ex.GetBaseException().Message, options.Broker.ReconnectInterval);
            }
            return false;
        }
        // Declares the exchange (AutoProvision), as a normal start does.
        await transport.InitializeAsync(runtime);
        logger.LogInformation("Events: connected to the RabbitMQ broker after the start of finance");
        return true;
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

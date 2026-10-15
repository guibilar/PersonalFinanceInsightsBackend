using Wolverine.RabbitMQ.Internal;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace Finance.Infrastructure.Events;

/// <summary>The connection to the RabbitMQ broker (<c>Events:Broker</c>, T37).</summary>
public sealed class FinanceBrokerOptions
{
    /// <summary>
    /// How long finance tries to reach the broker when it starts (Wolverine's <c>BrokerInitializationTimeout</c>; its
    /// default is 2 minutes, and finance does not answer requests during that time). A broker that is still down then
    /// does not stop the start (T79): finance starts without it, keeps its events, and
    /// <see cref="FinanceBrokerReconnector"/> connects later.
    /// </summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often <see cref="FinanceBrokerReconnector"/> tries to connect, when the broker was down at the start (T79).
    /// </summary>
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(15);

    public bool IsValid() => StartTimeout >= TimeSpan.Zero && ReconnectInterval > TimeSpan.Zero;
}

/// <summary>The state of the RabbitMQ transport of finance.</summary>
public static class FinanceBroker
{
    /// <summary>The RabbitMQ transport, or null without the finance events.</summary>
    public static RabbitMqTransport? Transport(IWolverineRuntime runtime) =>
        runtime.Options.Transports.OfType<RabbitMqTransport>().FirstOrDefault();

    /// <summary>
    /// True when the transport has an open connection to the broker. A transport that never connected (the broker was
    /// down at the start, T79) has none, whatever the status says: Wolverine keeps its connection object during a connect
    /// (the same rule as Hestia T90).
    /// </summary>
    public static async Task<bool> ConnectedAsync(IWolverineRuntime runtime, CancellationToken cancellationToken)
    {
        if (Transport(runtime) is not { } transport)
        {
            return false;
        }
        var probe = await transport.ProbeAsync(cancellationToken);
        return probe.Status is BrokerHealthStatus.Healthy or BrokerHealthStatus.Degraded && ConnectedOnce(probe);
    }

    /// <summary>True when the transport connected once, at the start or later.</summary>
    public static bool ConnectedOnce(BrokerHealthSnapshot probe) =>
        probe.Status != BrokerHealthStatus.Unknown && probe.LastSuccessfulAt != DateTimeOffset.MinValue;
}

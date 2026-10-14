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
    /// stops the start (see the README, "The broker is down"); Docker starts finance again (<c>restart</c>).
    /// </summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsValid() => StartTimeout >= TimeSpan.Zero;
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

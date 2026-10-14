using Finance.Infrastructure.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wolverine.Runtime;

namespace FinanceApi.Health;

/// <summary>
/// The readiness check <c>events-broker</c> (T37). <b>Degraded</b> (not Unhealthy, so <c>/health/ready</c> stays 200 and
/// finance works) while finance has no connection to the RabbitMQ broker: the events wait in the outbox. Healthy when
/// the events are off (no <c>Events:RabbitMq</c>).
/// </summary>
public sealed class BrokerHealthCheck(IServiceProvider services) : IHealthCheck
{
    public const string Name = "events-broker";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (services.GetService<IWolverineRuntime>() is not { } runtime || FinanceBroker.Transport(runtime) is null)
        {
            return HealthCheckResult.Healthy("The finance events are off (Events:RabbitMq is empty).");
        }
        return await FinanceBroker.ConnectedAsync(runtime, cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("Finance has no connection to the RabbitMQ broker. The events wait in the outbox; " +
                "finance sends them when the broker is back.");
    }
}

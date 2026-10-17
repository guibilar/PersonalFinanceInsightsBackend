using System.Collections.Concurrent;
using Finance.Application.Events;
using Hestia.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinanceTests;

/// <summary>
/// The finance events on, without a broker or an outbox: the services work as with the events on (each change in
/// FinanceChanges), and every event they raise is kept here.
/// </summary>
public sealed class RecordedEvents : IFinanceEvents
{
    public ConcurrentQueue<IIntegrationEvent> All { get; } = new();

    public bool Enabled => true;

    public void Raise(IIntegrationEvent @event) => All.Enqueue(@event);

    public void Clear() { }

    public void Register(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton(new FinanceEventsSwitch(Enabled: true)));
        services.Replace(ServiceDescriptor.Scoped<IFinanceEvents>(_ => this));
    }
}

using Hestia.Contracts;

namespace Finance.Application.Events;

/// <summary>
/// The events of the current request (T04, D7). A service raises an event before the save that stores its change, or
/// inside a <see cref="FinanceChanges"/> unit, and the infrastructure writes the event to the outbox in that save: the
/// change and the event commit together, or neither does. After the commit, the event goes to RabbitMQ.
/// </summary>
public interface IFinanceEvents
{
    /// <summary>
    /// False when finance has no events (no <c>Events:RabbitMq</c> setting): <see cref="Raise"/> does nothing, and the
    /// services skip the work that only finds events (budget thresholds, price changes).
    /// </summary>
    bool Enabled { get; }

    /// <summary>Queues an event for the next save of the request.</summary>
    void Raise(IIntegrationEvent @event);

    /// <summary>Forgets the queued events (a unit of work starts again after a database failure).</summary>
    void Clear();
}

/// <summary>Whether the finance events are on. The infrastructure turns them on (AddFinanceEvents).</summary>
public sealed record FinanceEventsSwitch(bool Enabled);

/// <summary>
/// The scoped list of raised events. The outbox interceptor in the infrastructure takes them in each save, and gives
/// them back when the save fails.
/// </summary>
public sealed class FinanceEventCollector(FinanceEventsSwitch eventsSwitch) : IFinanceEvents
{
    private readonly List<IIntegrationEvent> _pending = [];

    public bool Enabled => eventsSwitch.Enabled;

    public void Raise(IIntegrationEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (Enabled)
        {
            _pending.Add(@event);
        }
    }

    public void Clear() => _pending.Clear();

    /// <summary>Takes the events for the save that starts now.</summary>
    public IReadOnlyList<IIntegrationEvent> TakePending()
    {
        var events = _pending.ToList();
        _pending.Clear();
        return events;
    }

    /// <summary>The save failed: its events come first again, for a later save.</summary>
    public void Return(IReadOnlyList<IIntegrationEvent> events) => _pending.InsertRange(0, events);
}

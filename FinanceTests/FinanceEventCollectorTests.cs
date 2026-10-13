using Finance.Application.Events;
using Hestia.Contracts.Finance;

namespace FinanceTests;

/// <summary>The events of a request (T04), with their ids (Hestia T36). No broker or database is needed.</summary>
public class FinanceEventCollectorTests
{
    private static TransactionChanged Changed() => new(Guid.NewGuid(), TransactionChange.Added, Guid.NewGuid(),
        new DateOnly(2026, 10, 1), "COFFEE", -4.25m, Guid.NewGuid(), "Dining", DateTimeOffset.UtcNow);

    [Fact]
    public void EachNewEvent_HasItsOwnEventId()
    {
        var first = Changed();
        var second = Changed();

        Assert.NotEqual(Guid.Empty, first.EventId);
        Assert.NotEqual(first.EventId, second.EventId);
    }

    [Fact]
    public void AnEventWithoutAnEventId_IsRefused()
    {
        var collector = new FinanceEventCollector(new FinanceEventsSwitch(true));

        Assert.Throws<ArgumentException>(() => collector.Raise(Changed() with { EventId = Guid.Empty }));
        Assert.Empty(collector.TakePending());
    }

    [Fact]
    public void AnEvent_KeepsItsEventId_InTheQueue()
    {
        var collector = new FinanceEventCollector(new FinanceEventsSwitch(true));
        var changed = Changed();

        collector.Raise(changed);

        Assert.Equal(changed.EventId, Assert.Single(collector.TakePending()).EventId);
    }
}

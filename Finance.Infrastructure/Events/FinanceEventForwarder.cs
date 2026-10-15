using Hestia.Contracts.Finance;
using Wolverine;

namespace Finance.Infrastructure.Events;

/// <summary>
/// Sends one finance event from the durable local queue <see cref="FinanceEventsSetup.ForwardQueue"/> to the exchange
/// <see cref="FinanceEventTopology.Exchange"/> (T79). The sender is durable: the event becomes an outgoing envelope in
/// the finance database, and Wolverine sends it, also after a broker outage. Before the first connection to the broker,
/// the send throws: Wolverine keeps the event in the queue and tries again later (the retry policy of
/// <see cref="FinanceEventsSetup"/>).
/// </summary>
public sealed class FinanceEventForwarder<TEvent> where TEvent : class
{
    public static readonly Uri Exchange = new($"rabbitmq://exchange/{FinanceEventTopology.Exchange}");

    public async Task Handle(TEvent message, IMessageContext context) =>
        await context.EndpointFor(Exchange).SendAsync(message);
}

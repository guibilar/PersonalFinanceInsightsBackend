using System.Text.Json;
using System.Text.Json.Serialization;
using Hestia.Contracts.Finance;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

[assembly: AssemblyFixture(typeof(FinanceTests.TestRabbitMq))]

namespace FinanceTests;

/// <summary>
/// Picks the RabbitMQ broker for the whole test run from FINANCE_TEST_RABBITMQ:
/// unset — no broker: the event tests are skipped;
/// "container" — a RabbitMQ started in Docker with Testcontainers, removed after the run;
/// anything else — the amqp:// address of a broker you run yourself.
/// </summary>
public sealed class TestRabbitMq : IAsyncLifetime
{
    public const string Variable = "FINANCE_TEST_RABBITMQ";

    // The same major version as docker-compose.yml.
    private const string Image = "rabbitmq:4-alpine";

    private RabbitMqContainer? _container;

    /// <summary>The broker address, or null when the run has no broker.</summary>
    public static string? Address { get; private set; }

    public static bool Available => Address is not null;

    public async ValueTask InitializeAsync()
    {
        var setting = Environment.GetEnvironmentVariable(Variable);
        if (string.Equals(setting, "container", StringComparison.OrdinalIgnoreCase))
        {
            _container = new RabbitMqBuilder(Image).Build();
            await _container.StartAsync();
            Address = _container.GetConnectionString();
        }
        else
        {
            Address = string.IsNullOrWhiteSpace(setting) ? null : setting;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>
/// A private queue bound to the finance exchange, as Hestia.Api's queue is: it gets every event that finance sends.
/// Other tests run at the same time on the same broker, so look for an event by its values.
/// </summary>
public sealed class FinanceEventQueue : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly string _queue;
    private readonly List<(string? Type, byte[] Body)> _received = [];

    private FinanceEventQueue(IConnection connection, IChannel channel, string queue)
    {
        _connection = connection;
        _channel = channel;
        _queue = queue;
    }

    /// <summary>Call it after the finance host started: the host declares the exchange.</summary>
    public static async Task<FinanceEventQueue> BindAsync()
    {
        var connection = await new ConnectionFactory { Uri = new Uri(TestRabbitMq.Address!) }.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        var queue = await channel.QueueDeclareAsync(queue: "", durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue.QueueName, FinanceEventTopology.Exchange, routingKey: "");
        return new FinanceEventQueue(connection, channel, queue.QueueName);
    }

    /// <summary>Waits for an event of type <typeparamref name="T"/> that matches. Fails after the timeout.</summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool> match, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            await ReceiveAsync();
            if (Events<T>().FirstOrDefault(match) is { } found)
            {
                return found;
            }
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"No matching {typeof(T).Name} came. Types received: " +
                                           string.Join(", ", _received.Select(m => m.Type)));
            }
            await Task.Delay(100);
        }
    }

    /// <summary>Waits a short time, then returns the events of type <typeparamref name="T"/> that match (to show that none came).</summary>
    public async Task<IReadOnlyList<T>> AfterQuietPeriodAsync<T>(Func<T, bool> match)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        await ReceiveAsync();
        return Events<T>().Where(match).ToList();
    }

    private IEnumerable<T> Events<T>() => _received
        .Where(m => m.Type is { } type && (type == typeof(T).FullName || type == typeof(T).Name || type.EndsWith("." + typeof(T).Name, StringComparison.Ordinal)))
        .Select(m => JsonSerializer.Deserialize<T>(m.Body, Json)!);

    private async Task ReceiveAsync()
    {
        while (await _channel.BasicGetAsync(_queue, autoAck: true) is { } message)
        {
            _received.Add((message.BasicProperties.Type, message.Body.ToArray()));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        await _connection.CloseAsync();
        _channel.Dispose();
        _connection.Dispose();
    }
}

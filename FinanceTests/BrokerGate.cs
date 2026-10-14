using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace FinanceTests;

/// <summary>
/// A TCP gate in front of the test broker (T37): a host that connects to <see cref="Address"/> reaches the broker only
/// while the gate is open. Closed, the port refuses connections, as a broker that is down. <see cref="Close"/> also
/// cuts the open connections, as a broker that stops.
/// </summary>
public sealed class BrokerGate : IAsyncDisposable
{
    private readonly Uri _target;
    private readonly int _port;
    private readonly ConcurrentBag<TcpClient> _connections = [];
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;

    /// <param name="target">The broker address, for example TestRabbitMq.Address.</param>
    public BrokerGate(string target)
    {
        _target = new Uri(target);
        // A free port, then released: nothing listens on it until Open.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var address = new UriBuilder(_target) { Host = "127.0.0.1", Port = _port };
        Address = address.Uri.ToString();
    }

    /// <summary>The broker address through the gate, with the user and the password of the target.</summary>
    public string Address { get; }

    public bool IsOpen => _listener is not null;

    /// <summary>Starts to accept connections and forwards them to the broker.</summary>
    public void Open()
    {
        if (_listener is not null)
        {
            return;
        }
        _stop = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();
        _ = AcceptAsync(_listener, _stop.Token);
    }

    /// <summary>Refuses new connections and cuts the open ones.</summary>
    public void Close()
    {
        _stop?.Cancel();
        _listener?.Stop();
        _listener = null;
        while (_connections.TryTake(out var connection))
        {
            connection.Dispose();
        }
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop);
            }
            catch (Exception) when (stop.IsCancellationRequested || listener.Server is not { IsBound: true })
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
            _ = ForwardAsync(client, stop);
        }
    }

    private async Task ForwardAsync(TcpClient client, CancellationToken stop)
    {
        var broker = new TcpClient();
        _connections.Add(client);
        _connections.Add(broker);
        try
        {
            await broker.ConnectAsync(_target.Host, _target.Port, stop);
            var up = client.GetStream().CopyToAsync(broker.GetStream(), stop);
            var down = broker.GetStream().CopyToAsync(client.GetStream(), stop);
            await Task.WhenAny(up, down);
        }
        catch (Exception)
        {
            // A closed gate, or a side that went away.
        }
        finally
        {
            client.Dispose();
            broker.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Close();
        return ValueTask.CompletedTask;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace Impostor.Tools.OriginalEndpointRelay;

internal sealed class RelayRuntime : IAsyncDisposable
{
    private readonly List<RelayListener> _listeners;

    private RelayRuntime(List<RelayListener> listeners)
    {
        _listeners = listeners;
    }

    public static async Task<RelayRuntime> StartAsync(RelayConfig config, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        var reporter = new MetadataReporter(
            config.GetMetadataUri(),
            config.GetRelayApiKey(),
            httpClient);

        var listeners = new List<RelayListener>();

        if (config.IsEnabled(AddressFamily.InterNetwork))
        {
            listeners.Add(await RelayListener.CreateAsync(config, reporter, AddressFamily.InterNetwork, cancellationToken));
        }

        if (config.IsEnabled(AddressFamily.InterNetworkV6))
        {
            listeners.Add(await RelayListener.CreateAsync(config, reporter, AddressFamily.InterNetworkV6, cancellationToken));
        }

        foreach (var listener in listeners)
        {
            listener.Start(cancellationToken);
        }

        return new RelayRuntime(listeners);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var listener in _listeners)
        {
            await listener.DisposeAsync();
        }
    }
}

internal sealed class RelayListener : IAsyncDisposable
{
    private readonly Socket _listenerSocket;
    private readonly IPEndPoint _listenEndpoint;
    private readonly IPEndPoint _upstreamEndpoint;
    private readonly IPAddress _advertisedProxyIp;
    private readonly TimeSpan _idleTimeout;
    private readonly MetadataReporter _reporter;
    private readonly ConcurrentDictionary<IPEndPoint, RelaySession> _sessions = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _receiveLoopTask;
    private Task? _cleanupLoopTask;

    private RelayListener(
        Socket listenerSocket,
        IPEndPoint listenEndpoint,
        IPEndPoint upstreamEndpoint,
        IPAddress advertisedProxyIp,
        TimeSpan idleTimeout,
        MetadataReporter reporter)
    {
        _listenerSocket = listenerSocket;
        _listenEndpoint = listenEndpoint;
        _upstreamEndpoint = upstreamEndpoint;
        _advertisedProxyIp = advertisedProxyIp;
        _idleTimeout = idleTimeout;
        _reporter = reporter;
    }

    public static async Task<RelayListener> CreateAsync(
        RelayConfig config,
        MetadataReporter reporter,
        AddressFamily family,
        CancellationToken cancellationToken)
    {
        var listenEndpoint = new IPEndPoint(config.GetListenIp(family), config.ListenPort);
        var upstreamHost = config.GetUpstreamHost(family);
        var upstreamAddress = await ResolveUpstreamAddressAsync(upstreamHost, family, cancellationToken);
        var upstreamEndpoint = new IPEndPoint(upstreamAddress, config.UpstreamPort);

        var listenerSocket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        if (family == AddressFamily.InterNetworkV6)
        {
            listenerSocket.DualMode = false;
        }

        listenerSocket.Bind(listenEndpoint);

        Console.WriteLine(
            $"{DateTimeOffset.UtcNow:O} listening on {listenEndpoint} -> {upstreamEndpoint} ({family})");

        return new RelayListener(
            listenerSocket,
            listenEndpoint,
            upstreamEndpoint,
            config.GetAdvertisedProxyIp(family),
            config.GetIdleTimeout(),
            reporter);
    }

    public void Start(CancellationToken cancellationToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        _receiveLoopTask = RunReceiveLoopAsync(linkedCts.Token);
        _cleanupLoopTask = RunCleanupLoopAsync(linkedCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _listenerSocket.Dispose();

        if (_receiveLoopTask != null)
        {
            await IgnoreShutdownErrorsAsync(_receiveLoopTask);
        }

        if (_cleanupLoopTask != null)
        {
            await IgnoreShutdownErrorsAsync(_cleanupLoopTask);
        }

        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }
    }

    private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];

        while (!cancellationToken.IsCancellationRequested)
        {
            EndPoint remoteEndPoint = _listenEndpoint.AddressFamily == AddressFamily.InterNetwork
                ? new IPEndPoint(IPAddress.Any, 0)
                : new IPEndPoint(IPAddress.IPv6Any, 0);

            SocketReceiveFromResult result;
            try
            {
                result = await _listenerSocket.ReceiveFromAsync(
                    buffer,
                    SocketFlags.None,
                    remoteEndPoint,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex) when (IsShutdownError(ex.SocketErrorCode))
            {
                break;
            }

            var clientEndPoint = CloneEndpoint((IPEndPoint)result.RemoteEndPoint);
            var payload = new byte[result.ReceivedBytes];
            Buffer.BlockCopy(buffer, 0, payload, 0, result.ReceivedBytes);

            RelaySession session;
            try
            {
                session = _sessions.GetOrAdd(clientEndPoint, static (endpoint, state) => state!.CreateSession(endpoint), this);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTimeOffset.UtcNow:O} failed to create session for {clientEndPoint}: {ex}");
                continue;
            }

            try
            {
                await session.SendToUpstreamAsync(payload, cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException or HttpRequestException or OperationCanceledException)
            {
                Console.WriteLine($"{DateTimeOffset.UtcNow:O} relay send failed for {clientEndPoint}: {ex.Message}");
                if (_sessions.TryRemove(clientEndPoint, out var removed))
                {
                    await removed.DisposeAsync();
                }
            }
        }
    }

    private async Task RunCleanupLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _idleTimeout.TotalSeconds / 2)));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var utcNow = DateTimeOffset.UtcNow;

            foreach (var pair in _sessions)
            {
                if (utcNow - pair.Value.LastActivityUtc < _idleTimeout)
                {
                    continue;
                }

                if (_sessions.TryRemove(pair.Key, out var session))
                {
                    Console.WriteLine($"{DateTimeOffset.UtcNow:O} closing idle session {pair.Key}");
                    await session.DisposeAsync();
                }
            }
        }
    }

    private RelaySession CreateSession(IPEndPoint clientEndPoint)
    {
        return new RelaySession(
            _listenerSocket,
            clientEndPoint,
            _upstreamEndpoint,
            _advertisedProxyIp,
            _reporter);
    }

    private static async Task<IPAddress> ResolveUpstreamAddressAsync(string host, AddressFamily family, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literalAddress))
        {
            if (literalAddress.AddressFamily != family)
            {
                throw new InvalidOperationException($"Upstream host {host} resolved to {literalAddress.AddressFamily}, expected {family}.");
            }

            return literalAddress;
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var address = addresses.FirstOrDefault(x => x.AddressFamily == family);
        if (address == null)
        {
            throw new InvalidOperationException($"Unable to resolve {host} as {family}.");
        }

        return address;
    }

    private static IPEndPoint CloneEndpoint(IPEndPoint endpoint)
    {
        return new IPEndPoint(endpoint.Address, endpoint.Port);
    }

    private static bool IsShutdownError(SocketError error)
    {
        return error is SocketError.OperationAborted or SocketError.Interrupted or SocketError.NotSocket;
    }

    private static async Task IgnoreShutdownErrorsAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }
}

internal sealed class RelaySession : IAsyncDisposable
{
    private readonly Socket _listenerSocket;
    private readonly Socket _upstreamSocket;
    private readonly IPEndPoint _clientEndPoint;
    private readonly MetadataReporter _reporter;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly OriginalEndpointMetadata _metadata;
    private volatile bool _initialized;
    private Task? _receiveLoopTask;

    public RelaySession(
        Socket listenerSocket,
        IPEndPoint clientEndPoint,
        IPEndPoint upstreamEndpoint,
        IPAddress advertisedProxyIp,
        MetadataReporter reporter)
    {
        _listenerSocket = listenerSocket;
        _clientEndPoint = clientEndPoint;
        _reporter = reporter;
        _upstreamSocket = new Socket(upstreamEndpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        if (upstreamEndpoint.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _upstreamSocket.DualMode = false;
        }

        _upstreamSocket.Bind(new IPEndPoint(advertisedProxyIp, 0));
        _upstreamSocket.Connect(upstreamEndpoint);
        LastActivityUtc = DateTimeOffset.UtcNow;

        var proxyEndPoint = (IPEndPoint)_upstreamSocket.LocalEndPoint!;
        _metadata = new OriginalEndpointMetadata(
            proxyEndPoint.Address.ToString(),
            proxyEndPoint.Port,
            clientEndPoint.Address.ToString(),
            clientEndPoint.Port,
            clientEndPoint.AddressFamily.ToString(),
            DateTimeOffset.UtcNow);
    }

    public DateTimeOffset LastActivityUtc { get; private set; }

    public async Task SendToUpstreamAsync(byte[] payload, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        LastActivityUtc = DateTimeOffset.UtcNow;
        await _upstreamSocket.SendAsync(payload, SocketFlags.None, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _upstreamSocket.Dispose();

        if (_receiveLoopTask != null)
        {
            try
            {
                await _receiveLoopTask;
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _reporter.PostAsync(_metadata, cancellationToken);
            _receiveLoopTask = RunReceiveLoopAsync(_disposeCts.Token);
            _initialized = true;

            Console.WriteLine(
                $"{DateTimeOffset.UtcNow:O} mapped proxy {_metadata.ProxyAddress}:{_metadata.ProxyPort} -> client {_metadata.ClientAddress}:{_metadata.ClientPort}");
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];

        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await _upstreamSocket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.OperationAborted or SocketError.Interrupted or SocketError.NotSocket)
            {
                break;
            }

            LastActivityUtc = DateTimeOffset.UtcNow;
            await _listenerSocket.SendToAsync(
                buffer.AsMemory(0, bytesRead),
                SocketFlags.None,
                _clientEndPoint,
                cancellationToken);
        }
    }
}

internal sealed class MetadataReporter
{
    private readonly Uri _metadataUri;
    private readonly string _relayApiKey;
    private readonly HttpClient _httpClient;

    public MetadataReporter(Uri metadataUri, string relayApiKey, HttpClient httpClient)
    {
        _metadataUri = metadataUri;
        _relayApiKey = relayApiKey;
        _httpClient = httpClient;
    }

    public async Task PostAsync(OriginalEndpointMetadata metadata, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _metadataUri)
        {
            Content = JsonContent.Create(metadata, OriginalEndpointMetadataContext.Default.OriginalEndpointMetadata),
        };

        request.Headers.Add("X-Relay-Key", _relayApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

internal sealed record OriginalEndpointMetadata(
    string ProxyAddress,
    int ProxyPort,
    string ClientAddress,
    int ClientPort,
    string AddressFamily,
    DateTimeOffset ObservedAtUtc);

[JsonSerializable(typeof(OriginalEndpointMetadata))]
internal sealed partial class OriginalEndpointMetadataContext : JsonSerializerContext
{
}

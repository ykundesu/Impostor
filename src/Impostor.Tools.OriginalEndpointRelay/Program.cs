using System.Net.Sockets;

namespace Impostor.Tools.OriginalEndpointRelay;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var config = RelayConfig.Load(args);
            using var shutdown = new CancellationTokenSource();

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };

            await using var runtime = await RelayRuntime.StartAsync(config, shutdown.Token);

            Console.WriteLine($"{DateTimeOffset.UtcNow:O} relay started");
            await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex) when (ex is SocketException or HttpRequestException or InvalidOperationException)
        {
            Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} relay failed: {ex}");
            return 1;
        }
    }
}

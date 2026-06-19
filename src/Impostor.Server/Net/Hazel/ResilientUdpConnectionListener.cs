using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Impostor.Hazel;
using Impostor.Hazel.Udp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

namespace Impostor.Server.Net.Hazel
{
    /// <summary>
    ///     A <see cref="UdpConnectionListener"/> that never lets a single packet take down the
    ///     whole listen loop.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In the upstream Impostor.Hazel listener, the <c>catch</c> that logs "Listen loop error"
    ///         sits OUTSIDE the receive <c>while</c> loop. If <see cref="ProcessData"/> throws, the
    ///         exception unwinds the whole loop, gets logged once, and the listen task ends — there is
    ///         no restart, so the single shared UDP socket stops receiving for every lobby on the
    ///         server until the process is restarted.
    ///     </para>
    ///     <para>
    ///         The realistic trigger is a race: <c>ProcessData</c> fetches a connection from the
    ///         dictionary and then awaits a write to that connection's channel
    ///         (<c>Pipeline.Writer.WriteAsync</c>). If the connection is concurrently disposed
    ///         (a kick/leave/keep-alive timeout completes the channel) in the window between the two,
    ///         the write throws <see cref="System.Threading.Channels.ChannelClosedException"/>. Because
    ///         UDP is connectionless, in-flight packets from a just-disconnected client keep arriving,
    ///         so the window is reachable in normal operation and can also be hit deliberately by a
    ///         client that disconnects and floods packets from the same endpoint.
    ///     </para>
    ///     <para>
    ///         Wrapping the <c>ProcessData</c> call per packet keeps the loop alive on any per-packet
    ///         failure — matching the original willardf/Hazel-Networking behaviour, which catches
    ///         around each message and continues. The dropped packet is harmless: it targets a
    ///         connection that is already gone, and UDP makes no delivery guarantee anyway.
    ///     </para>
    /// </remarks>
    internal sealed class ResilientUdpConnectionListener : UdpConnectionListener
    {
        private readonly ILogger<ResilientUdpConnectionListener> _logger;

        public ResilientUdpConnectionListener(
            IPEndPoint endPoint,
            ObjectPool<MessageReader> readerPool,
            ILogger<ResilientUdpConnectionListener> logger,
            IPMode ipMode = IPMode.IPv4)
            : base(endPoint, readerPool, ipMode)
        {
            _logger = logger;
        }

        protected override async ValueTask ProcessData(UdpReceiveResult data)
        {
            try
            {
                await base.ProcessData(data);
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                // The target connection was disposed while this packet was in flight. Expected for
                // UDP; drop the packet and keep the listen loop running. Not logged: an attacker
                // could otherwise flood the log by racing disconnects against packet bursts.
            }
            catch (System.Exception ex)
            {
                // Never let a single malformed/racing packet take down the shared listen loop.
                _logger.LogWarning(ex, "Dropped UDP packet from {EndPoint} due to an error while processing", data.RemoteEndPoint);
            }
        }
    }
}

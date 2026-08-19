using System.Collections.Generic;

namespace Impostor.Api.Statistics
{
    /// <summary>
    /// Provides cumulative RPC telemetry snapshots since process start.
    /// </summary>
    public interface IRpcTelemetryProvider
    {
        RpcTelemetrySnapshot GetSnapshot();
    }

    public sealed class RpcTelemetrySnapshot
    {
        public RpcTelemetrySnapshot(
            long totalReceived,
            IReadOnlyDictionary<byte, long> receivedByRpcId,
            IReadOnlyDictionary<int, RoomRpcTelemetrySnapshot> rooms)
        {
            TotalReceived = totalReceived;
            ReceivedByRpcId = receivedByRpcId;
            Rooms = rooms;
        }

        public long TotalReceived { get; }

        public IReadOnlyDictionary<byte, long> ReceivedByRpcId { get; }

        public IReadOnlyDictionary<int, RoomRpcTelemetrySnapshot> Rooms { get; }
    }

}

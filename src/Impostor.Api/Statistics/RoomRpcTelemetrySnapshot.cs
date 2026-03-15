using System.Collections.Generic;

namespace Impostor.Api.Statistics
{
    public sealed class RoomRpcTelemetrySnapshot
    {
        public RoomRpcTelemetrySnapshot(
            int roomCode,
            long totalReceived,
            IReadOnlyDictionary<byte, long> receivedByRpcId)
        {
            RoomCode = roomCode;
            TotalReceived = totalReceived;
            ReceivedByRpcId = receivedByRpcId;
        }

        public int RoomCode { get; }

        public long TotalReceived { get; }

        public IReadOnlyDictionary<byte, long> ReceivedByRpcId { get; }
    }
}

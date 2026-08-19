using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Impostor.Api.Statistics;

namespace Impostor.Server.Statistics
{
    internal sealed class RpcTelemetryProvider : IRpcTelemetryProvider
    {
        private const int RpcIdCount = byte.MaxValue + 1;

        private readonly long[] _receivedByRpcId = new long[RpcIdCount];
        private readonly ConcurrentDictionary<int, RoomCounters> _rooms = new ConcurrentDictionary<int, RoomCounters>();
        private long _totalReceived;

        public void RecordReceived(int roomCode, byte rpcId)
        {
            Interlocked.Increment(ref _totalReceived);
            Interlocked.Increment(ref _receivedByRpcId[rpcId]);

            var roomCounters = _rooms.GetOrAdd(roomCode, static code => new RoomCounters(code));
            roomCounters.Record(rpcId);
        }

        public void RemoveRoom(int roomCode)
        {
            _rooms.TryRemove(roomCode, out _);
        }

        public RpcTelemetrySnapshot GetSnapshot()
        {
            var rooms = new Dictionary<int, RoomRpcTelemetrySnapshot>(_rooms.Count);

            foreach (var room in _rooms)
            {
                rooms[room.Key] = room.Value.GetSnapshot();
            }

            return new RpcTelemetrySnapshot(
                Interlocked.Read(ref _totalReceived),
                CreateSnapshot(_receivedByRpcId),
                rooms);
        }

        private static IReadOnlyDictionary<byte, long> CreateSnapshot(long[] counts)
        {
            var snapshot = new Dictionary<byte, long>();

            for (var i = 0; i < counts.Length; i++)
            {
                var value = Interlocked.Read(ref counts[i]);
                if (value > 0)
                {
                    snapshot[(byte)i] = value;
                }
            }

            return snapshot;
        }

        private sealed class RoomCounters
        {
            private readonly long[] _receivedByRpcId = new long[RpcIdCount];
            private long _totalReceived;

            public RoomCounters(int roomCode)
            {
                RoomCode = roomCode;
            }

            public int RoomCode { get; }

            public void Record(byte rpcId)
            {
                Interlocked.Increment(ref _totalReceived);
                Interlocked.Increment(ref _receivedByRpcId[rpcId]);
            }

            public RoomRpcTelemetrySnapshot GetSnapshot()
            {
                return new RoomRpcTelemetrySnapshot(
                    RoomCode,
                    Interlocked.Read(ref _totalReceived),
                    CreateSnapshot(_receivedByRpcId));
            }
        }
    }
}

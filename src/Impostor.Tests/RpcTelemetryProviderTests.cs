using Impostor.Server.Statistics;
using Xunit;

namespace Impostor.Tests
{
    public class RpcTelemetryProviderTests
    {
        [Fact]
        public void RecordReceived_ShouldTrackGlobalAndRoomCounts()
        {
            var provider = new RpcTelemetryProvider();

            provider.RecordReceived(123456, 14);
            provider.RecordReceived(123456, 14);
            provider.RecordReceived(123456, 24);
            provider.RecordReceived(654321, 14);

            var snapshot = provider.GetSnapshot();

            Assert.Equal(4, snapshot.TotalReceived);
            Assert.Equal(3, snapshot.ReceivedByRpcId[14]);
            Assert.Equal(1, snapshot.ReceivedByRpcId[24]);

            Assert.Equal(2, snapshot.Rooms.Count);
            Assert.Equal(3, snapshot.Rooms[123456].TotalReceived);
            Assert.Equal(2, snapshot.Rooms[123456].ReceivedByRpcId[14]);
            Assert.Equal(1, snapshot.Rooms[123456].ReceivedByRpcId[24]);
            Assert.Equal(1, snapshot.Rooms[654321].TotalReceived);
            Assert.Equal(1, snapshot.Rooms[654321].ReceivedByRpcId[14]);
        }

        [Fact]
        public void RemoveRoom_ShouldDropRoomSnapshotWithoutTouchingGlobalCounters()
        {
            var provider = new RpcTelemetryProvider();

            provider.RecordReceived(123456, 14);
            provider.RecordReceived(123456, 24);
            provider.RemoveRoom(123456);

            var snapshot = provider.GetSnapshot();

            Assert.Equal(2, snapshot.TotalReceived);
            Assert.Equal(1, snapshot.ReceivedByRpcId[14]);
            Assert.Equal(1, snapshot.ReceivedByRpcId[24]);
            Assert.Empty(snapshot.Rooms);
        }
    }
}

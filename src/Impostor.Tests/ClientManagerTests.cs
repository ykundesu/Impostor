#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Impostor.Api.Config;
using Impostor.Api.Events;
using Impostor.Api.Events.Managers;
using Impostor.Api.Games;
using Impostor.Api.Innersloth;
using Impostor.Api.Net;
using Impostor.Api.Net.Manager;
using Impostor.Api.Utils;
using Impostor.Hazel;
using Impostor.Hazel.Abstractions;
using Impostor.Server.Http;
using Impostor.Server.Net;
using Impostor.Server.Net.Factories;
using Impostor.Server.Net.Manager;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using CompatibilityGroup = Impostor.Api.Net.Manager.ICompatibilityManager.CompatibilityGroup;
using VersionCompareResult = Impostor.Api.Net.Manager.ICompatibilityManager.VersionCompareResult;

namespace Impostor.Tests
{
    public sealed class ClientManagerTests
    {
        [Fact]
        public async Task RegisterConnectionAsync_MatchesTokenUsingOriginalEndpoint()
        {
            var timeProvider = new TestDateTimeProvider(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));
            var tokenTracker = new MatchmakingTokenTracker(timeProvider);
            tokenTracker.Record(IPAddress.Parse("2001:db8::25"), "product-user-id", "player", 20260316);

            var connection = new TestHazelConnection(
                new IPEndPoint(IPAddress.Parse("198.51.100.10"), 45000),
                new IPEndPoint(IPAddress.Parse("2001:db8::25"), 51234));
            var clientFactory = new TestClientFactory();
            var manager = new ClientManager(
                NullLogger<ClientManager>.Instance,
                new TestEventManager(),
                clientFactory,
                new TestCompatibilityManager(),
                Options.Create(new CompatibilityConfig()),
                tokenTracker);

            await manager.RegisterConnectionAsync(
                connection,
                "player",
                new GameVersion(2026, 3, 16),
                Language.English,
                QuickChatModes.QuickChatOnly,
                new PlatformSpecificData(Platforms.StandaloneSteamPC, "Steam"));

            Assert.True(clientFactory.CreatedClient.Items.TryGetValue(MatchmakingTokenTracker.ClientItemKey, out var value));
            var tokenRecord = Assert.IsType<MatchmakingTokenRecord>(value);
            Assert.Equal("product-user-id", tokenRecord.ProductUserId);
        }

        private sealed class TestClientFactory : IClientFactory
        {
            public TestClient CreatedClient { get; private set; } = null!;

            public ClientBase Create(IHazelConnection connection, string name, GameVersion clientVersion, Language language, QuickChatModes chatMode, PlatformSpecificData platformSpecificData)
            {
                CreatedClient = new TestClient(name, clientVersion, language, chatMode, platformSpecificData, connection);
                connection.Client = CreatedClient;
                return CreatedClient;
            }
        }

        private sealed class TestClient : ClientBase
        {
            public TestClient(string name, GameVersion gameVersion, Language language, QuickChatModes chatMode, PlatformSpecificData platformSpecificData, IHazelConnection connection)
                : base(name, gameVersion, language, chatMode, platformSpecificData, connection)
            {
            }

            public override ValueTask HandleMessageAsync(IMessageReader message, MessageType messageType)
            {
                return default;
            }

            public override ValueTask HandleDisconnectAsync(string reason)
            {
                return default;
            }
        }

        private sealed class TestHazelConnection : IHazelConnection
        {
            public TestHazelConnection(IPEndPoint endPoint, IPEndPoint? originalEndPoint)
            {
                EndPoint = endPoint;
                OriginalEndPoint = originalEndPoint;
            }

            public IPEndPoint EndPoint { get; }

            public IPEndPoint? OriginalEndPoint { get; }

            public bool IsConnected => true;

            public IClient? Client { get; set; }

            public float AveragePing => 0;

            public ValueTask SendAsync(IMessageWriter writer)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask DisconnectAsync(string? reason, IMessageWriter? writer = null)
            {
                return ValueTask.CompletedTask;
            }
        }

        private sealed class TestEventManager : IEventManager
        {
            public IDisposable RegisterListener<TListener>(TListener listener, Func<Func<Task>, Task>? invoker = null)
                where TListener : IEventListener
            {
                return new TestDisposable();
            }

            public bool IsRegistered<TEvent>()
                where TEvent : IEvent
            {
                return false;
            }

            public ValueTask CallAsync<TEvent>(TEvent @event)
                where TEvent : IEvent
            {
                return default;
            }

            private sealed class TestDisposable : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }

        private sealed class TestCompatibilityManager : ICompatibilityManager
        {
            public IEnumerable<CompatibilityGroup> CompatibilityGroups => Array.Empty<CompatibilityGroup>();

            public VersionCompareResult CanConnectToServer(GameVersion clientVersion)
            {
                return VersionCompareResult.Compatible;
            }

            public GameJoinError CanJoinGame(GameVersion hostVersion, GameVersion clientVersion)
            {
                return GameJoinError.None;
            }

            public void AddCompatibilityGroup(CompatibilityGroup compatibilityGroup)
            {
            }

            public void AddSupportedVersion(CompatibilityGroup compatibilityGroup, GameVersion gameVersion)
            {
            }

            public bool RemoveSupportedVersion(GameVersion removedVersion)
            {
                return false;
            }
        }

        private sealed class TestDateTimeProvider : IDateTimeProvider
        {
            public TestDateTimeProvider(DateTimeOffset utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTimeOffset UtcNow { get; set; }
        }
    }
}

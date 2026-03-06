using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Innersloth;
using Impostor.Api.Innersloth.Customization;
using Impostor.Api.Net;
using Impostor.Server.Net.State;

namespace Impostor.Server.Net
{
    internal abstract class ClientBase : IClient
    {
        private readonly object _pendingDisconnectLock = new object();
        private DisconnectReason? _pendingDisconnectReason;
        private string? _pendingDisconnectDetail;
        private string? _pendingCustomDisconnectMessage;

        protected ClientBase(string name, GameVersion gameVersion, Language language, QuickChatModes chatMode, PlatformSpecificData platformSpecificData, IHazelConnection connection)
        {
            Name = name;
            GameVersion = gameVersion;
            Language = language;
            ChatMode = chatMode;
            PlatformSpecificData = platformSpecificData;
            Connection = connection;
            Items = new ConcurrentDictionary<object, object>();
        }

        public int Id { get; set; }

        public string Name { get; }

        public Language Language { get; }

        public QuickChatModes ChatMode { get; }

        public PlatformSpecificData PlatformSpecificData { get; }

        public GameVersion GameVersion { get; }

        public IHazelConnection Connection { get; }

        public IDictionary<object, object> Items { get; }

        public ClientPlayer? Player { get; set; }

        public ColorType? PreviousColor { get; set; } = null;

        IClientPlayer? IClient.Player => Player;

        public virtual ValueTask<bool> ReportCheatAsync(CheatContext context, CheatCategory category, string message)
        {
            return new ValueTask<bool>(false);
        }

        public ValueTask<bool> ReportCheatAsync(CheatContext context, string message)
        {
            return ReportCheatAsync(context, CheatCategory.Other, message);
        }

        public abstract ValueTask HandleMessageAsync(IMessageReader message, MessageType messageType);

        public abstract ValueTask HandleDisconnectAsync(string reason);

        public async ValueTask DisconnectAsync(DisconnectReason reason, string? message = null)
        {
            if (!Connection.IsConnected)
            {
                return;
            }

            var detail = GetPendingDisconnectDetail(reason);

            lock (_pendingDisconnectLock)
            {
                _pendingDisconnectReason = reason;
                _pendingDisconnectDetail = detail;
                _pendingCustomDisconnectMessage = reason == DisconnectReason.Custom ? message : null;
            }

            await Connection.CustomDisconnectAsync(reason, message);
        }

        internal async ValueTask DisconnectWithReasonDetailAsync(DisconnectReason reason, string detail, string? message = null)
        {
            if (!Connection.IsConnected)
            {
                return;
            }

            SetPendingDisconnectDetail(reason, detail);

            lock (_pendingDisconnectLock)
            {
                _pendingDisconnectReason = reason;
                _pendingDisconnectDetail = detail;
                _pendingCustomDisconnectMessage = reason == DisconnectReason.Custom ? message : null;
            }

            await Connection.CustomDisconnectAsync(reason, message);
        }

        internal void SetPendingDisconnectDetail(DisconnectReason reason, string detail)
        {
            lock (_pendingDisconnectLock)
            {
                _pendingDisconnectReason = reason;
                _pendingDisconnectDetail = detail;
            }
        }

        internal string? GetPendingDisconnectDetail(DisconnectReason reason)
        {
            lock (_pendingDisconnectLock)
            {
                return _pendingDisconnectReason == reason ? _pendingDisconnectDetail : null;
            }
        }

        protected (DisconnectReason? Reason, string? Detail, string? CustomMessage) ConsumePendingDisconnectContext()
        {
            lock (_pendingDisconnectLock)
            {
                var reason = _pendingDisconnectReason;
                var detail = _pendingDisconnectDetail;
                var customMessage = _pendingCustomDisconnectMessage;

                _pendingDisconnectReason = null;
                _pendingDisconnectDetail = null;
                _pendingCustomDisconnectMessage = null;

                return (reason, detail, customMessage);
            }
        }

        public bool Equals(IClient? other)
        {
            return other != null && Id == other.Id;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as ClientBase);
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }
}

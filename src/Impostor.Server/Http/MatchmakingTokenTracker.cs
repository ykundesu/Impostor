using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using Impostor.Api.Innersloth;
using Impostor.Api.Net;
using Impostor.Api.Utils;

namespace Impostor.Server.Http
{
    public sealed class MatchmakingTokenTracker
    {
        internal static readonly object ClientItemKey = new object();

        private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);
        private readonly ConcurrentDictionary<IPAddress, ConcurrentQueue<MatchmakingTokenRecord>> _records = new ConcurrentDictionary<IPAddress, ConcurrentQueue<MatchmakingTokenRecord>>();
        private readonly IDateTimeProvider _dateTimeProvider;

        public MatchmakingTokenTracker(IDateTimeProvider dateTimeProvider)
        {
            _dateTimeProvider = dateTimeProvider;
        }

        internal void Record(IPAddress ipAddress, string productUserId, string username, int clientVersion)
        {
            var queue = _records.GetOrAdd(ipAddress, _ => new ConcurrentQueue<MatchmakingTokenRecord>());
            Prune(queue);
            queue.Enqueue(new MatchmakingTokenRecord(productUserId, username, clientVersion, _dateTimeProvider.UtcNow));
        }

        internal bool TryMatch(IPAddress ipAddress, string username, GameVersion clientVersion, out MatchmakingTokenRecord? record)
        {
            record = null;

            if (!_records.TryGetValue(ipAddress, out var queue))
            {
                return false;
            }

            Prune(queue);

            var candidates = queue
                .ToArray()
                .OrderByDescending(x => x.IssuedAt)
                .ToArray();

            record = candidates.FirstOrDefault(x =>
                x.ClientVersion == clientVersion.Value &&
                string.Equals(x.Username, username, StringComparison.Ordinal));

            if (record != null)
            {
                return true;
            }

            if (candidates.Length == 1)
            {
                record = candidates[0];
                return true;
            }

            return false;
        }

        internal static bool TryGetMatchedToken(IClient client, out MatchmakingTokenRecord? record)
        {
            record = null;

            if (client.Items.TryGetValue(ClientItemKey, out var value) && value is MatchmakingTokenRecord matchedToken)
            {
                record = matchedToken;
                return true;
            }

            return false;
        }

        private void Prune(ConcurrentQueue<MatchmakingTokenRecord> queue)
        {
            while (queue.TryPeek(out var record) && _dateTimeProvider.UtcNow - record.IssuedAt > Retention)
            {
                queue.TryDequeue(out _);
            }
        }
    }

    internal sealed class MatchmakingTokenRecord
    {
        public MatchmakingTokenRecord(string productUserId, string username, int clientVersion, DateTimeOffset issuedAt)
        {
            ProductUserId = productUserId;
            Username = username;
            ClientVersion = clientVersion;
            IssuedAt = issuedAt;
        }

        public string ProductUserId { get; }

        public string Username { get; }

        public int ClientVersion { get; }

        public DateTimeOffset IssuedAt { get; }
    }
}

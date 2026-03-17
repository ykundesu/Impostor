using System;
using System.Collections.Concurrent;
using System.Net;
using Impostor.Api.Config;
using Impostor.Api.Utils;
using Microsoft.Extensions.Options;

namespace Impostor.Server.Http
{
    public sealed class OriginalEndpointTracker
    {
        private readonly ConcurrentDictionary<OriginalEndpointKey, OriginalEndpointRecord> _records = new();
        private readonly IDateTimeProvider _dateTimeProvider;

        public OriginalEndpointTracker(IDateTimeProvider dateTimeProvider, IOptions<OriginalEndpointConfig> config)
        {
            _dateTimeProvider = dateTimeProvider;
            Retention = TimeSpan.FromSeconds(Math.Max(1, config.Value.RetentionSeconds));
        }

        public TimeSpan Retention { get; }

        public void Record(IPEndPoint proxyEndPoint, IPEndPoint clientEndPoint, DateTimeOffset observedAtUtc)
        {
            var now = _dateTimeProvider.UtcNow;
            PruneExpired(now);
            _records[OriginalEndpointKey.From(proxyEndPoint)] = new OriginalEndpointRecord(clientEndPoint, observedAtUtc);
        }

        public bool TryResolve(IPEndPoint proxyEndPoint, out IPEndPoint? clientEndPoint)
        {
            var now = _dateTimeProvider.UtcNow;
            PruneExpired(now);

            if (_records.TryRemove(OriginalEndpointKey.From(proxyEndPoint), out var record) && !IsExpired(record.ObservedAtUtc, now))
            {
                clientEndPoint = record.ClientEndPoint;
                return true;
            }

            clientEndPoint = null;
            return false;
        }

        private void PruneExpired(DateTimeOffset now)
        {
            foreach (var pair in _records)
            {
                if (IsExpired(pair.Value.ObservedAtUtc, now))
                {
                    _records.TryRemove(pair.Key, out _);
                }
            }
        }

        private bool IsExpired(DateTimeOffset observedAtUtc, DateTimeOffset now)
        {
            return now - observedAtUtc > Retention;
        }

        private readonly record struct OriginalEndpointKey(IPAddress Address, int Port)
        {
            public static OriginalEndpointKey From(IPEndPoint endPoint)
            {
                return new OriginalEndpointKey(endPoint.Address, endPoint.Port);
            }
        }

        private readonly record struct OriginalEndpointRecord(IPEndPoint ClientEndPoint, DateTimeOffset ObservedAtUtc);
    }
}

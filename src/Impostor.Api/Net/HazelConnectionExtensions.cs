using System;
using System.Net;

namespace Impostor.Api.Net
{
    public static class HazelConnectionExtensions
    {
        public static IPEndPoint GetEffectiveEndPoint(this IHazelConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.OriginalEndPoint ?? connection.EndPoint;
        }
    }
}

using System;

namespace Impostor.Api.Config
{
    public class OriginalEndpointConfig
    {
        public const string Section = "OriginalEndpoint";

        public bool Enabled { get; set; }

        public string? RelayApiKey { get; set; }

        public string[] TrustedProxyAddresses { get; set; } = Array.Empty<string>();

        public int RetentionSeconds { get; set; } = 60;
    }
}

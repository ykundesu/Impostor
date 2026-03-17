using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace Impostor.Tools.OriginalEndpointRelay;

internal sealed class RelayConfig
{
    public const string SectionName = "Relay";
    private const string EnvironmentPrefix = "IMPOSTOR_ORIGINAL_ENDPOINT_RELAY_";

    public string? ListenIpV4 { get; init; }

    public string? ListenIpV6 { get; init; }

    public ushort ListenPort { get; init; } = 22023;

    public string? UpstreamHostV4 { get; init; }

    public string? UpstreamHostV6 { get; init; }

    public ushort UpstreamPort { get; init; } = 22023;

    public string? AdvertisedProxyIpV4 { get; init; }

    public string? AdvertisedProxyIpV6 { get; init; }

    public string? MetadataUrl { get; init; }

    public string? RelayApiKey { get; init; }

    public int IdleTimeoutSeconds { get; init; } = 60;

    public static RelayConfig Load(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables(EnvironmentPrefix)
            .Build();

        var config = configuration.GetSection(SectionName).Get<RelayConfig>() ?? new RelayConfig();
        config.Validate();
        return config;
    }

    public TimeSpan GetIdleTimeout()
    {
        return TimeSpan.FromSeconds(IdleTimeoutSeconds);
    }

    public bool IsEnabled(AddressFamily family)
    {
        return family switch
        {
            AddressFamily.InterNetwork => !string.IsNullOrWhiteSpace(ListenIpV4) &&
                !string.IsNullOrWhiteSpace(UpstreamHostV4) &&
                !string.IsNullOrWhiteSpace(AdvertisedProxyIpV4),
            AddressFamily.InterNetworkV6 => !string.IsNullOrWhiteSpace(ListenIpV6) &&
                !string.IsNullOrWhiteSpace(UpstreamHostV6) &&
                !string.IsNullOrWhiteSpace(AdvertisedProxyIpV6),
            _ => false,
        };
    }

    public IPAddress GetListenIp(AddressFamily family)
    {
        return ParseIpAddress(family == AddressFamily.InterNetwork ? ListenIpV4 : ListenIpV6, family, "ListenIp");
    }

    public IPAddress GetAdvertisedProxyIp(AddressFamily family)
    {
        return ParseIpAddress(
            family == AddressFamily.InterNetwork ? AdvertisedProxyIpV4 : AdvertisedProxyIpV6,
            family,
            "AdvertisedProxyIp");
    }

    public string GetUpstreamHost(AddressFamily family)
    {
        var value = family == AddressFamily.InterNetwork ? UpstreamHostV4 : UpstreamHostV6;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Upstream host is not configured for {family}.");
        }

        return value.Trim();
    }

    public Uri GetMetadataUri()
    {
        if (!Uri.TryCreate(MetadataUrl, UriKind.Absolute, out var metadataUri))
        {
            throw new InvalidOperationException("Relay:MetadataUrl must be an absolute URI.");
        }

        return metadataUri;
    }

    public string GetRelayApiKey()
    {
        if (string.IsNullOrWhiteSpace(RelayApiKey))
        {
            throw new InvalidOperationException("Relay:RelayApiKey must be configured.");
        }

        return RelayApiKey.Trim();
    }

    private void Validate()
    {
        if (!IsEnabled(AddressFamily.InterNetwork) && !IsEnabled(AddressFamily.InterNetworkV6))
        {
            throw new InvalidOperationException("At least one of IPv4 or IPv6 relay settings must be configured.");
        }

        if (ListenPort == 0)
        {
            throw new InvalidOperationException("Relay:ListenPort must be greater than 0.");
        }

        if (UpstreamPort == 0)
        {
            throw new InvalidOperationException("Relay:UpstreamPort must be greater than 0.");
        }

        if (IdleTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Relay:IdleTimeoutSeconds must be greater than 0.");
        }

        _ = GetMetadataUri();
        _ = GetRelayApiKey();

        if (IsEnabled(AddressFamily.InterNetwork))
        {
            _ = GetListenIp(AddressFamily.InterNetwork);
            _ = GetAdvertisedProxyIp(AddressFamily.InterNetwork);
        }

        if (IsEnabled(AddressFamily.InterNetworkV6))
        {
            _ = GetListenIp(AddressFamily.InterNetworkV6);
            _ = GetAdvertisedProxyIp(AddressFamily.InterNetworkV6);
        }
    }

    private static IPAddress ParseIpAddress(string? value, AddressFamily family, string propertyName)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != family)
        {
            throw new InvalidOperationException($"Relay:{propertyName} for {family} must be a literal {family} address.");
        }

        return address;
    }
}

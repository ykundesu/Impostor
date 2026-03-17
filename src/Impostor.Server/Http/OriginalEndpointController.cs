using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Impostor.Api.Config;
using Impostor.Api.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Impostor.Server.Http;

[Route("/api/internal/original-endpoint")]
[ApiController]
public sealed class OriginalEndpointController : ControllerBase
{
    private const string RelayApiKeyHeaderName = "X-Relay-Key";

    private readonly OriginalEndpointTracker _tracker;
    private readonly OriginalEndpointConfig _config;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<OriginalEndpointController> _logger;

    public OriginalEndpointController(
        OriginalEndpointTracker tracker,
        IOptions<OriginalEndpointConfig> config,
        IDateTimeProvider dateTimeProvider,
        ILogger<OriginalEndpointController> logger)
    {
        _tracker = tracker;
        _config = config.Value;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult Register([FromBody] OriginalEndpointRegistrationRequest request)
    {
        if (!_config.Enabled)
        {
            return NotFound();
        }

        var remoteIpAddress = HttpContext.Connection.RemoteIpAddress;
        if (remoteIpAddress == null || !IsTrustedProxyAddress(remoteIpAddress))
        {
            _logger.LogWarning("Rejected original endpoint metadata from untrusted proxy {RemoteIpAddress}.", remoteIpAddress);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!IsAuthorizedRelay())
        {
            _logger.LogWarning("Rejected original endpoint metadata due to invalid relay key from {RemoteIpAddress}.", remoteIpAddress);
            return Unauthorized();
        }

        if (!TryParseRegistration(request, out var proxyEndPoint, out var clientEndPoint, out var errorMessage))
        {
            return BadRequest(errorMessage);
        }

        var now = _dateTimeProvider.UtcNow;
        if (request.ObservedAtUtc < now - _tracker.Retention || request.ObservedAtUtc > now + _tracker.Retention)
        {
            return BadRequest("observedAtUtc is outside the accepted time window.");
        }

        _tracker.Record(proxyEndPoint!, clientEndPoint!, request.ObservedAtUtc);
        return NoContent();
    }

    private bool IsAuthorizedRelay()
    {
        var expectedApiKey = _config.RelayApiKey;
        var providedApiKey = HttpContext.Request.Headers[RelayApiKeyHeaderName].ToString();

        if (string.IsNullOrWhiteSpace(expectedApiKey) || string.IsNullOrWhiteSpace(providedApiKey))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);
        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private bool IsTrustedProxyAddress(IPAddress remoteIpAddress)
    {
        foreach (var configuredAddress in _config.TrustedProxyAddresses)
        {
            if (IPAddress.TryParse(configuredAddress, out var trustedAddress) && trustedAddress.Equals(remoteIpAddress))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseRegistration(
        OriginalEndpointRegistrationRequest request,
        out IPEndPoint? proxyEndPoint,
        out IPEndPoint? clientEndPoint,
        out string? errorMessage)
    {
        proxyEndPoint = null;
        clientEndPoint = null;

        if (!TryParseAddressFamily(request.AddressFamily, out var addressFamily))
        {
            errorMessage = "addressFamily must be InterNetwork or InterNetworkV6.";
            return false;
        }

        if (!IPAddress.TryParse(request.ProxyAddress, out var proxyAddress))
        {
            errorMessage = "proxyAddress must be a valid IP literal.";
            return false;
        }

        if (!IPAddress.TryParse(request.ClientAddress, out var clientAddress))
        {
            errorMessage = "clientAddress must be a valid IP literal.";
            return false;
        }

        if (proxyAddress.AddressFamily != addressFamily || clientAddress.AddressFamily != addressFamily)
        {
            errorMessage = "proxyAddress, clientAddress, and addressFamily must match.";
            return false;
        }

        proxyEndPoint = new IPEndPoint(proxyAddress, request.ProxyPort);
        clientEndPoint = new IPEndPoint(clientAddress, request.ClientPort);
        errorMessage = null;
        return true;
    }

    private static bool TryParseAddressFamily(string addressFamily, out System.Net.Sockets.AddressFamily parsedAddressFamily)
    {
        if (string.Equals(addressFamily, nameof(System.Net.Sockets.AddressFamily.InterNetwork), StringComparison.Ordinal))
        {
            parsedAddressFamily = System.Net.Sockets.AddressFamily.InterNetwork;
            return true;
        }

        if (string.Equals(addressFamily, nameof(System.Net.Sockets.AddressFamily.InterNetworkV6), StringComparison.Ordinal))
        {
            parsedAddressFamily = System.Net.Sockets.AddressFamily.InterNetworkV6;
            return true;
        }

        parsedAddressFamily = default;
        return false;
    }

    public sealed class OriginalEndpointRegistrationRequest
    {
        [JsonPropertyName("proxyAddress")]
        public required string ProxyAddress { get; init; }

        [JsonPropertyName("proxyPort")]
        public required int ProxyPort { get; init; }

        [JsonPropertyName("clientAddress")]
        public required string ClientAddress { get; init; }

        [JsonPropertyName("clientPort")]
        public required int ClientPort { get; init; }

        [JsonPropertyName("addressFamily")]
        public required string AddressFamily { get; init; }

        [JsonPropertyName("observedAtUtc")]
        public required DateTimeOffset ObservedAtUtc { get; init; }
    }
}

using System;
using System.Net;
using Impostor.Api.Config;
using Impostor.Api.Utils;
using Impostor.Server.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Impostor.Tests
{
    public sealed class OriginalEndpointControllerTests
    {
        [Fact]
        public void Register_StoresIpv4OriginalEndpoint()
        {
            var now = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);
            var dateTimeProvider = new TestDateTimeProvider(now);
            var tracker = CreateTracker(dateTimeProvider, 60);
            var controller = CreateController(tracker, dateTimeProvider, new OriginalEndpointConfig
            {
                Enabled = true,
                RelayApiKey = "relay-secret",
                TrustedProxyAddresses = new[] { "198.51.100.10" },
                RetentionSeconds = 60,
            });

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(IPAddress.Parse("198.51.100.10"), "relay-secret"),
            };

            var result = controller.Register(new OriginalEndpointController.OriginalEndpointRegistrationRequest
            {
                ProxyAddress = "198.51.100.10",
                ProxyPort = 45000,
                ClientAddress = "203.0.113.25",
                ClientPort = 51234,
                AddressFamily = "InterNetwork",
                ObservedAtUtc = now,
            });

            Assert.IsType<NoContentResult>(result);
            Assert.True(tracker.TryResolve(new IPEndPoint(IPAddress.Parse("198.51.100.10"), 45000), out var originalEndPoint));
            Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.25"), 51234), originalEndPoint);
        }

        [Fact]
        public void Register_StoresIpv6OriginalEndpoint()
        {
            var now = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);
            var dateTimeProvider = new TestDateTimeProvider(now);
            var tracker = CreateTracker(dateTimeProvider, 60);
            var controller = CreateController(tracker, dateTimeProvider, new OriginalEndpointConfig
            {
                Enabled = true,
                RelayApiKey = "relay-secret",
                TrustedProxyAddresses = new[] { "2001:db8::100" },
                RetentionSeconds = 60,
            });

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(IPAddress.Parse("2001:db8::100"), "relay-secret"),
            };

            var result = controller.Register(new OriginalEndpointController.OriginalEndpointRegistrationRequest
            {
                ProxyAddress = "2001:db8::100",
                ProxyPort = 45000,
                ClientAddress = "2001:db8::25",
                ClientPort = 51234,
                AddressFamily = "InterNetworkV6",
                ObservedAtUtc = now,
            });

            Assert.IsType<NoContentResult>(result);
            Assert.True(tracker.TryResolve(new IPEndPoint(IPAddress.Parse("2001:db8::100"), 45000), out var originalEndPoint));
            Assert.Equal(new IPEndPoint(IPAddress.Parse("2001:db8::25"), 51234), originalEndPoint);
        }

        [Fact]
        public void Register_RejectsInvalidRelayKey()
        {
            var dateTimeProvider = new TestDateTimeProvider(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));
            var tracker = CreateTracker(dateTimeProvider, 60);
            var controller = CreateController(tracker, dateTimeProvider, new OriginalEndpointConfig
            {
                Enabled = true,
                RelayApiKey = "relay-secret",
                TrustedProxyAddresses = new[] { "198.51.100.10" },
                RetentionSeconds = 60,
            });

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(IPAddress.Parse("198.51.100.10"), "wrong-secret"),
            };

            var result = controller.Register(new OriginalEndpointController.OriginalEndpointRegistrationRequest
            {
                ProxyAddress = "198.51.100.10",
                ProxyPort = 45000,
                ClientAddress = "203.0.113.25",
                ClientPort = 51234,
                AddressFamily = "InterNetwork",
                ObservedAtUtc = dateTimeProvider.UtcNow,
            });

            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public void Register_RejectsUntrustedProxyAddress()
        {
            var dateTimeProvider = new TestDateTimeProvider(new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero));
            var tracker = CreateTracker(dateTimeProvider, 60);
            var controller = CreateController(tracker, dateTimeProvider, new OriginalEndpointConfig
            {
                Enabled = true,
                RelayApiKey = "relay-secret",
                TrustedProxyAddresses = new[] { "198.51.100.10" },
                RetentionSeconds = 60,
            });

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(IPAddress.Parse("198.51.100.99"), "relay-secret"),
            };

            var result = controller.Register(new OriginalEndpointController.OriginalEndpointRegistrationRequest
            {
                ProxyAddress = "198.51.100.10",
                ProxyPort = 45000,
                ClientAddress = "203.0.113.25",
                ClientPort = 51234,
                AddressFamily = "InterNetwork",
                ObservedAtUtc = dateTimeProvider.UtcNow,
            });

            var statusCode = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, statusCode.StatusCode);
        }

        [Fact]
        public void Register_RejectsExpiredTimestamp()
        {
            var now = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);
            var dateTimeProvider = new TestDateTimeProvider(now);
            var tracker = CreateTracker(dateTimeProvider, 60);
            var controller = CreateController(tracker, dateTimeProvider, new OriginalEndpointConfig
            {
                Enabled = true,
                RelayApiKey = "relay-secret",
                TrustedProxyAddresses = new[] { "198.51.100.10" },
                RetentionSeconds = 60,
            });

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(IPAddress.Parse("198.51.100.10"), "relay-secret"),
            };

            var result = controller.Register(new OriginalEndpointController.OriginalEndpointRegistrationRequest
            {
                ProxyAddress = "198.51.100.10",
                ProxyPort = 45000,
                ClientAddress = "203.0.113.25",
                ClientPort = 51234,
                AddressFamily = "InterNetwork",
                ObservedAtUtc = now.AddMinutes(-2),
            });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public void Register_RejectsAddressFamilyMismatch()
        {
            var now = new DateTimeOffset(2026, 3, 16, 0, 0, 0, TimeSpan.Zero);
            var dateTimeProvider = new TestDateTimeProvider(now);
            var tracker = CreateTracker(dateTimeProvider, 60);
            var controller = CreateController(tracker, dateTimeProvider, new OriginalEndpointConfig
            {
                Enabled = true,
                RelayApiKey = "relay-secret",
                TrustedProxyAddresses = new[] { "2001:db8::100" },
                RetentionSeconds = 60,
            });

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(IPAddress.Parse("2001:db8::100"), "relay-secret"),
            };

            var result = controller.Register(new OriginalEndpointController.OriginalEndpointRegistrationRequest
            {
                ProxyAddress = "2001:db8::100",
                ProxyPort = 45000,
                ClientAddress = "2001:db8::25",
                ClientPort = 51234,
                AddressFamily = "InterNetwork",
                ObservedAtUtc = now,
            });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        private static OriginalEndpointTracker CreateTracker(TestDateTimeProvider dateTimeProvider, int retentionSeconds)
        {
            return new OriginalEndpointTracker(dateTimeProvider, Options.Create(new OriginalEndpointConfig
            {
                RetentionSeconds = retentionSeconds,
            }));
        }

        private static OriginalEndpointController CreateController(
            OriginalEndpointTracker tracker,
            TestDateTimeProvider dateTimeProvider,
            OriginalEndpointConfig config)
        {
            return new OriginalEndpointController(
                tracker,
                Options.Create(config),
                dateTimeProvider,
                NullLogger<OriginalEndpointController>.Instance);
        }

        private static HttpContext CreateHttpContext(IPAddress remoteIpAddress, string relayApiKey)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = remoteIpAddress;
            context.Request.Headers["X-Relay-Key"] = relayApiKey;
            return context;
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

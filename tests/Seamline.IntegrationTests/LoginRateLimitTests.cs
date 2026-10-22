using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Seamline.IntegrationTests;

// The login limiter is partitioned per client IP, and behind trusted proxies
// the client IP comes from X-Forwarded-For (ADR-0030). TestServer has no
// real connection, so a startup filter pins RemoteIpAddress to 10.0.0.5 —
// inside the proxy network the tests configure — and each request then
// looks exactly like one arriving through nginx.
public sealed class LoginRateLimitTests(SeamlineApiFactory factory) : IClassFixture<SeamlineApiFactory>
{
    [Fact]
    public async Task Each_forwarded_client_ip_gets_its_own_login_window()
    {
        using var limited = CreateLimitedFactory(knownNetworks: "10.0.0.0/8", forwardLimit: 1);
        var client = limited.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginFromAsync(client, "203.0.113.1"));

        // A different client behind the same proxy is unaffected.
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "203.0.113.2"));
    }

    [Fact]
    public async Task Without_known_proxy_networks_the_forwarded_header_is_ignored()
    {
        // No trusted proxies configured: a client rotating X-Forwarded-For
        // values must still share one window — its real address.
        using var limited = CreateLimitedFactory(knownNetworks: "", forwardLimit: 1);
        var client = limited.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "198.51.100.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "198.51.100.2"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginFromAsync(client, "198.51.100.3"));
    }

    [Fact]
    public async Task Behind_two_proxies_the_client_entry_is_used_and_anything_left_of_it_is_ignored()
    {
        // k8s shape: Traefik appends the client, nginx appends Traefik.
        // The leftmost entry is whatever the client sent — rotating it must
        // not buy a fresh window.
        using var limited = CreateLimitedFactory(knownNetworks: "10.0.0.0/8", forwardLimit: 2);
        var client = limited.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "198.51.100.1, 203.0.113.7, 10.0.0.9"));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "198.51.100.2, 203.0.113.7, 10.0.0.9"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await LoginFromAsync(client, "198.51.100.3, 203.0.113.7, 10.0.0.9"));

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginFromAsync(client, "198.51.100.4, 203.0.113.8, 10.0.0.9"));
    }

    private WebApplicationFactory<Program> CreateLimitedFactory(string knownNetworks, int forwardLimit) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:LoginPermitLimit"] = "2",
                ["ReverseProxy:KnownNetworks"] = knownNetworks,
                ["ReverseProxy:ForwardLimit"] = forwardLimit.ToString()
            }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ProxyRemoteAddressStartupFilter>());
        });

    private static async Task<HttpStatusCode> LoginFromAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new { tenantId = Guid.NewGuid(), login = "nobody", password = "wrong" })
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return (await client.SendAsync(request)).StatusCode;
    }

    private sealed class ProxyRemoteAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

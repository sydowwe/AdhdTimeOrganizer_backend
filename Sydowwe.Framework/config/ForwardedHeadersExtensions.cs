using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using IPNetwork = System.Net.IPNetwork;

namespace Sydowwe.Framework.config;

public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Configures forwarded-header processing so that <c>UseForwardedHeaders()</c> resolves a
    /// trustworthy client IP into <c>HttpContext.Connection.RemoteIpAddress</c>.
    /// <para>Trusted proxy networks/IPs are read from the <c>TRUSTED_PROXY_NETWORKS</c> env var
    /// (comma-separated CIDR or bare IPs, e.g. <c>"10.0.0.0/8,172.18.0.1"</c>). When unset, only
    /// loopback is trusted — which means behind a containerised reverse proxy the forwarded IP is
    /// ignored and every client collapses onto the proxy IP. You MUST set it in any real deployment;
    /// see <c>docs/setup.md</c>.</para>
    /// </summary>
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // One reverse-proxy hop. Raising this trusts more attacker-controllable left-hand entries.
            options.ForwardLimit = 1;

            var trusted = Environment.GetEnvironmentVariable("TRUSTED_PROXY_NETWORKS");
            if (string.IsNullOrWhiteSpace(trusted))
                return;

            // Replace the loopback-only defaults with the explicitly trusted proxy ranges.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var entry in trusted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (entry.Contains('/') && IPNetwork.TryParse(entry, out var network))
                    options.KnownIPNetworks.Add(network);
                else if (IPAddress.TryParse(entry, out var proxyIp))
                    options.KnownProxies.Add(proxyIp);
        });
        return services;
    }

    /// <summary>
    /// Pins the FastEndpoints request-throttle key to the framework-validated client IP.
    /// <para>FastEndpoints throttles on the <b>raw</b> <c>X-Forwarded-For</c> header, NOT
    /// <c>Connection.RemoteIpAddress</c>. Left alone that is bypassable: an attacker rotates the
    /// header to get a fresh bucket each request, and <c>UseForwardedHeaders()</c> consumes the entry
    /// it validates so the throttle is often left with no key at all (a 403 "Forbidden by rate
    /// limiting middleware!"). Overwriting the header with the resolved <c>RemoteIpAddress</c> — which
    /// <c>UseForwardedHeaders</c> only believes for trusted proxies — makes the throttle count per
    /// real client IP and ignore any forged/rotated value.</para>
    /// <para>MUST be registered immediately after <c>app.UseForwardedHeaders()</c> and before
    /// <c>app.UseFastEndpoints()</c>.</para>
    /// </summary>
    public static IApplicationBuilder UseClientIpThrottleKey(this IApplicationBuilder app)
        => app.Use(async (HttpContext context, Func<Task> next) =>
        {
            var clientIp = context.Connection.RemoteIpAddress;
            if (clientIp is not null)
                context.Request.Headers["X-Forwarded-For"] = clientIp.ToString();

            await next();
        });
}
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Sydowwe.Framework.application.middleware;

/// <summary>
/// Makes the client-IP header safe to use as a rate-limit key.
///
/// <para>FastEndpoints' <c>Throttle(limit, seconds, headerName)</c> buckets by a request header. Any
/// header the <i>client</i> controls is useless for that — an attacker just sends a new value per
/// request and gets unlimited attempts. This middleware <b>overwrites</b>
/// <see cref="ClientIpHeaderName"/> with the connection's resolved <c>RemoteIpAddress</c> on every
/// request, so whatever the client sent is discarded before any endpoint sees it.</para>
///
/// <para><b>Order matters:</b> register this <i>after</i> <c>UseForwardedHeaders()</c>, so
/// <c>RemoteIpAddress</c> is the real client rather than the reverse proxy. Registered before it, every
/// request behind a proxy shares one bucket — the proxy's address — and the limits become global
/// instead of per-client.</para>
/// </summary>
public class TrustedIpMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The one definition of the throttle-key header. Endpoints reference this rather than the string
    /// so the name cannot drift apart from the middleware that makes it trustworthy — a
    /// <c>Throttle(…, "X-Real-Ip")</c> typo silently buckets everyone together under a header nothing
    /// sets.
    /// </summary>
    public const string ClientIpHeaderName = "X-Real-IP";

    /// <summary>
    /// Client-supplied correlation id, dropped for the same reason: it reaches logs, so a client must
    /// not get to choose it.
    /// </summary>
    private const string ClientSuppliedCorrelationHeader = "X-Client-Id";

    public async Task InvokeAsync(HttpContext ctx)
    {
        ctx.Request.Headers[ClientIpHeaderName] = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        ctx.Request.Headers.Remove(ClientSuppliedCorrelationHeader);
        await next(ctx);
    }
}

public static class TrustedIpMiddlewareExtensions
{
    /// <summary>
    /// Stamps the trusted client-IP header used by every throttled endpoint. Call directly after
    /// <c>UseForwardedHeaders()</c>.
    /// </summary>
    public static IApplicationBuilder UseTrustedClientIpHeader(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TrustedIpMiddleware>();
    }
}

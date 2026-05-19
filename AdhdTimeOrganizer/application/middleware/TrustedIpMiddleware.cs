namespace AdhdTimeOrganizer.application.middleware;

public class TrustedIpMiddleware(RequestDelegate next)
{
    private const string ThrottleHeader = "X-Real-IP";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        ctx.Request.Headers[ThrottleHeader] = ip;
        ctx.Request.Headers.Remove("X-Client-Id");
        await next(ctx);
    }
}

using FastEndpoints;
using Microsoft.Extensions.Logging;

namespace AdhdTimeOrganizer.application.endpoint.@base;

public class ErrorLoggingPostProcessor : IGlobalPostProcessor
{
    public Task PostProcessAsync(IPostProcessorContext ctx, CancellationToken ct)
    {
        if (ctx.HttpContext.Response.StatusCode < 400)
            return Task.CompletedTask;

        var logger = ctx.HttpContext.RequestServices
            .GetRequiredService<ILogger<ErrorLoggingPostProcessor>>();

        var logLevel = ctx.HttpContext.Response.StatusCode >= 500 ? LogLevel.Error : LogLevel.Warning;

        if (ctx.ValidationFailures.Count > 0)
        {
            logger.Log(logLevel,
                "HTTP {Method} {Path} {StatusCode} - Validation errors: {Errors}",
                ctx.HttpContext.Request.Method,
                ctx.HttpContext.Request.Path,
                ctx.HttpContext.Response.StatusCode,
                ctx.ValidationFailures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"));
        }
        else
        {
            logger.Log(logLevel,
                "HTTP {Method} {Path} {StatusCode}",
                ctx.HttpContext.Request.Method,
                ctx.HttpContext.Request.Path,
                ctx.HttpContext.Response.StatusCode);
        }

        return Task.CompletedTask;
    }
}

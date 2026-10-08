using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.application.middleware;

/// <summary>
/// FO5: stamps <see cref="User.LastActiveAt"/> on an authenticated web-client request, at most once per
/// <see cref="UserPresence.StampInterval"/> per user, and opens the welcome-back card when the stamp it replaces is
/// at least <see cref="UserPresence.GreetAfter"/> old.
/// <para>
/// <b>Before the endpoint, not after.</b> The client's first requests after a holiday go out together, and
/// <c>GET /welcome-back</c> may be any of them. Whichever request stamps first records the gap in
/// <see cref="User.WelcomeBackAwayFrom"/> / <see cref="User.WelcomeBackReturnedAt"/>, and the card is read from those,
/// so it no longer matters which request reads <see cref="User.LastActiveAt"/> after it moved.
/// </para>
/// <para>
/// <b>One conditional UPDATE, no read.</b> The <c>WHERE</c> only matches a stale stamp, so concurrent first requests
/// race harmlessly: one of them moves the row and the others match nothing. Postgres evaluates every <c>SET</c>
/// against the old row, which is what lets the same statement copy the old stamp into the card before overwriting it.
/// <c>ExecuteUpdateAsync</c> also keeps the write off the Identity concurrency stamp, so a preferences save that
/// loaded the row a moment earlier still succeeds.
/// </para>
/// <para>
/// <b>Tracker tokens never stamp.</b> A desktop heartbeat every few minutes is the user at their computer, not in the
/// app; counting it would mean no tracker user is ever away.
/// </para>
/// <para>
/// <b>Never fails the request.</b> Presence is a nicety, and the request it rides on is the user's actual work. A
/// failed stamp is logged and the request goes on; the in-memory throttle still holds for the hour, so a broken
/// database does not get one extra write per request on top of everything else.
/// </para>
/// </summary>
public sealed class LastActiveStampMiddleware(
    RequestDelegate next,
    IMemoryCache throttle,
    TimeProvider time,
    ILogger<LastActiveStampMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirst(AuthClaims.ClientType)?.Value != AuthClaims.ExtensionClientType
            && context.User.GetIdOrNull() is { } userId)
            await StampAsync(context, userId);

        await next(context);
    }

    private async Task StampAsync(HttpContext context, long userId)
    {
        var key = $"fo5-last-active:{userId}";
        if (throttle.TryGetValue(key, out _))
            return;

        throttle.Set(key, true, UserPresence.StampInterval);

        try
        {
            var now = time.GetUtcNow().UtcDateTime;
            var staleBefore = now - UserPresence.StampInterval;
            var greetBefore = now - UserPresence.GreetAfter;

            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            await db.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == userId && (u.LastActiveAt == null || u.LastActiveAt < staleBefore))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.WelcomeBackAwayFrom,
                        u => u.LastActiveAt < greetBefore ? u.LastActiveAt : u.WelcomeBackAwayFrom)
                    .SetProperty(u => u.WelcomeBackReturnedAt,
                        u => u.LastActiveAt < greetBefore ? now : u.WelcomeBackReturnedAt)
                    .SetProperty(u => u.LastActiveAt, now), context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not stamp LastActiveAt for user {UserId}; the request continues", userId);
        }
    }
}

public static class LastActiveStampMiddlewareExtensions
{
    /// <inheritdoc cref="LastActiveStampMiddleware"/>
    public static IApplicationBuilder UseLastActiveStamp(this IApplicationBuilder app) =>
        app.UseMiddleware<LastActiveStampMiddleware>();
}

using AdhdTimeOrganizer.infrastructure.extService.googleCalendar;

namespace AdhdTimeOrganizer.application.endpoint.user.command.googleCalendar;

/// <summary>
/// The OAuth <c>state</c> cookie that <c>GET …/auth-url</c> sets and <c>POST …/connect</c> reads back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cookie's path is derived from the request, never written down.</b> It used to be the literal
/// <c>/user/google-calendar</c>, but FastEndpoints mounts every route under <c>RoutePrefix = "api"</c>,
/// so the browser's request is <c>/api/user/google-calendar/connect</c> — a path the literal does not
/// prefix, and a browser will not send a cookie to a path outside its scope. Every real connect was
/// therefore the "state is missing" 400 while the suite, which writes the <c>Cookie</c> header by
/// hand, stayed green (B14). Both endpoints live directly under one segment, so the parent of whichever
/// one is executing is the shared scope, prefix and path base included.
/// </para>
/// <para>
/// <c>SameSite=Strict</c> holds only if the SPA and the API are same-site (share a registrable domain):
/// <c>localhost:3000</c> ↔ <c>localhost:8080</c> in dev, <c>adhdtimeorganizer.com</c> ↔ its API host
/// in production. Serve the API from an unrelated domain and the cookie stops riding the POST.
/// </para>
/// </remarks>
public static class GoogleCalendarStateCookie
{
    public const string Name = GoogleCalendarService.StateCookieName;

    /// <summary>The parent of the executing endpoint's full path — <c>/api/user/google-calendar</c> in this host.</summary>
    public static string PathFor(HttpRequest request)
    {
        var fullPath = request.PathBase.Add(request.Path).Value ?? "/";
        var lastSlash = fullPath.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : fullPath[..lastSlash];
    }

    public static CookieOptions Options(HttpRequest request) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = PathFor(request),
        Expires = DateTimeOffset.UtcNow.AddMinutes(10),
        IsEssential = true
    };

    public static void Delete(HttpContext context)
        => context.Response.Cookies.Delete(Name, new CookieOptions { Path = PathFor(context.Request) });
}

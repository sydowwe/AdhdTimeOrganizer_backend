using FastEndpoints;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.user.read;

/// <summary>
/// Lists the authenticated user's active sessions, flagging the one the request arrives on.
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseGetUserSessionsEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest<IList<UserSessionResponse>>
{
    public override void Configure()
    {
        Get("/user/sessions");
        Summary(s => { s.Summary = "List all active sessions for the authenticated user"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var currentSessionHash = HttpContext.Request.Cookies[AuthCookies.SessionHashName];

        var sessions = await refreshTokenService.GetUserSessionsAsync(userId);

        var response = sessions.Select(s =>
        {
            var (device, browser) = UserAgentParser.Parse(s.UserAgent);
            return new UserSessionResponse
            {
                Id = s.Id,
                Device = device,
                Browser = browser,
                Ip = s.IpAddress,
                LastUsedAt = s.CreatedAt,
                CreatedAt = s.CreatedAt,
                IsCurrent = !string.IsNullOrEmpty(currentSessionHash) && s.TokenHash == currentSessionHash
            };
        }).ToList();

        await Send.OkAsync(response, ct);
    }
}
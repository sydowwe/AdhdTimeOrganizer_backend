using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.infrastructure.helpers;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

public class GetUserSessionsEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest<IList<UserSessionResponse>>
{
    public override void Configure()
    {
        Get("/user/sessions");
        Summary(s => { s.Summary = "List all active sessions for the authenticated user"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var currentSessionHash = HttpContext.Request.Cookies["session-hash"];

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

using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

public class ExtensionRefreshTokenEndpoint(IJwtService jwtService)
    : Endpoint<RefreshTokenRequest, RefreshTokenResponse>
{
    public override void Configure()
    {
        Post("auth/extension/refresh");
        AllowAnonymous();
        Summary(s => { s.Summary = "Refresh access token using refresh token from request body (extension clients)"; });
    }

    public override async Task HandleAsync(RefreshTokenRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.RefreshToken))
        {
            AddError("Refresh token not provided");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        try
        {
            var (accessToken, newRefreshToken, _) = await jwtService.RefreshTokensAsync(
                req.RefreshToken, isExtensionClient: true, HttpContext);

            var response = new RefreshTokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken
            };
            await Send.OkAsync(response, ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            AddError(ex.Message);
            await Send.ErrorsAsync(401, ct);
        }
    }
}

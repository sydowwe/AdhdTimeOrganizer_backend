using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.domain.extServiceContract.googleCalendar;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.application.endpoint.user.command.googleCalendar;

public class ConnectGoogleCalendarEndpoint(
    UserManager<User> userManager,
    IGoogleCalendarService googleCalendarService)
    : Endpoint<ConnectGoogleCalendarRequest>
{
    public override void Configure()
    {
        Post("/user/google-calendar/connect");
        Validator<ConnectGoogleCalendarValidator>();
        Summary(s => { s.Summary = "Connect Google Calendar by exchanging OAuth code for refresh token"; });
    }

    public override async Task HandleAsync(ConnectGoogleCalendarRequest req, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var hasStateCookie = HttpContext.Request.Cookies.TryGetValue(GoogleCalendarStateCookie.Name, out var expectedState);
        GoogleCalendarStateCookie.Delete(HttpContext);
        if (!hasStateCookie || string.IsNullOrEmpty(expectedState) || expectedState != req.State)
        {
            // Both are a 400 the client answers with "start over"; the type only says whether the
            // cookie never arrived (path / SameSite / expiry) or arrived with another flow's state.
            HttpContext.SetResultErrorType(hasStateCookie ? ResultErrorType.BadRequest : ResultErrorType.CookieMissing);
            AddError("The OAuth state is missing or does not match. Please restart the Google Calendar connection flow.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var refreshToken = await googleCalendarService.ExchangeCodeForRefreshToken(req.Code);
        if (refreshToken is null)
        {
            HttpContext.SetResultErrorType(ResultErrorType.ExternalServiceError);
            AddError("Failed to obtain refresh token from Google. Ensure access_type=offline and prompt=consent were used.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        user.GoogleCalendarRefreshToken = refreshToken;
        await userManager.UpdateAsync(user);

        await Send.NoContentAsync(ct);
    }
}
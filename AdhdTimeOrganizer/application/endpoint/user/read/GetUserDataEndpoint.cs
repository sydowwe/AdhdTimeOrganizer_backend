using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.read;
using Sydowwe.Framework.application.dto.response.user;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

/// <summary>
/// Returns the currently logged-in user's profile data, including this portal's own preference columns.
///
/// <para>Those columns are why the endpoint names <see cref="AppUserDataResponse"/> rather than deriving from
/// the single-parameter base: the client's settings screen writes <c>firstDayOfWeek</c> and
/// <c>weatherLocation</c> through <c>PUT /user/preferences</c> and has to be able to read them back to render
/// the controls. Dropping <see cref="Enrich"/> would not break anything visible here — the route still answers
/// 200 with a complete-looking body — the fields would simply stop arriving.</para>
/// </summary>
public class GetCurrentUserEndpoint(UserManager<User> userManager)
    : BaseGetCurrentUserEndpoint<User, AppUserDataResponse>(userManager)
{
    protected override AppUserDataResponse Enrich(UserDataResponse response, User user) =>
        new(response)
        {
            FirstDayOfWeek = user.FirstDayOfWeek,
            WeatherLocation = user.WeatherLocation
        };
}

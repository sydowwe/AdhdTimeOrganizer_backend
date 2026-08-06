using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.domain.entity.user;

namespace Sydowwe.Framework.application.endpoint.user.read;

public abstract class BaseGetCurrentUserEndpoint<TUser>(UserManager<TUser> userManager)
    : EndpointWithoutRequest<UserDataResponse>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Get("/user/data");
        Summary(s => { s.Summary = "Get full profile data for the authenticated user"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Personal data: never let a browser, proxy or CDN retain the body.
        HttpContext.Response.Headers.CacheControl = "no-store, private";

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var response = new UserDataResponse
        {
            Id = user.Id,
            Email = user.Email!,
            TwoFactorEnabled = user.TwoFactorEnabled,
            CreatedAt = user.CreatedTimestamp,
            Theme = user.Theme,
            Locale = user.Locale,
            Timezone = user.Timezone.Id,
            AskBeforeDelete = user.AskBeforeDelete
        };
        await Send.OkAsync(Enrich(response, user), ct);
    }

    /// <summary>
    /// Fills in response fields that live on the derived user type rather than <see cref="BaseUser"/>.
    /// Identity here; override with <c>response with { … }</c> to contribute them.
    ///
    /// <para>Every field <see cref="UserDataResponse"/> declares today is a <see cref="BaseUser"/> field,
    /// so the base fills all of them and no host currently overrides this. The seam is kept for the host
    /// that widens the response with a column of its own.</para>
    /// </summary>
    protected virtual UserDataResponse Enrich(UserDataResponse response, TUser user) => response;
}
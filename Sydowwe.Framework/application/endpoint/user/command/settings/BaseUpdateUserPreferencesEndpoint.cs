using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.domain.entity.user;

namespace Sydowwe.Framework.application.endpoint.user.command.settings;

/// <summary>
/// Partially updates the preference columns <see cref="BaseUser"/> owns. Hosts that store extra
/// preferences derive <see cref="UserPreferencesRequest"/> and override
/// <see cref="ApplyExtraPreferences"/>; everything else — route, unauthorized path, identity update
/// and its error mapping — is here.
///
/// <para>No validator is attached: the concrete request type differs per host, so the subclass wires
/// its own via <c>Validator&lt;…&gt;()</c> in an overridden <c>Configure</c>.</para>
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseUpdateUserPreferencesEndpoint<TUser, TRequest>(UserManager<TUser> userManager)
    : Endpoint<TRequest>
    where TUser : BaseUser
    where TRequest : UserPreferencesRequest
{
    public override void Configure()
    {
        Put("user/preferences");
        Summary(s => { s.Summary = "Partially update the current user's appearance and locale preferences"; });
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        if (req.Theme.HasValue)
            user.Theme = req.Theme.Value;
        if (req.Locale.HasValue)
            user.Locale = req.Locale.Value;
        if (req.AskBeforeDelete.HasValue)
            user.AskBeforeDelete = req.AskBeforeDelete.Value;

        if (req.Timezone != null)
            user.Timezone = TimeZoneInfo.FindSystemTimeZoneById(req.Timezone);

        ApplyExtraPreferences(user, req);

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                AddError(error.Description);
            await Send.ErrorsAsync(400, ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }

    /// <summary>
    /// Writes the host's own preference columns onto <paramref name="user"/>. Same "null means leave
    /// unchanged" convention as the base fields. Called before <c>UserManager.UpdateAsync</c>, so no
    /// separate save is needed.
    /// </summary>
    protected virtual void ApplyExtraPreferences(TUser user, TRequest req)
    {
    }
}
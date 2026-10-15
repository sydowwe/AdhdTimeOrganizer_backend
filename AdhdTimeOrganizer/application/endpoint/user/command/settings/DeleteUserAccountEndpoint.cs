using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.service.user;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using FastEndpoints;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.application.preprocessor;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

/// <summary>
/// <c>DELETE user/account</c> — <b>schedules</b> the deletion rather than performing it (P3-07); see
/// <see cref="IAccountDeletionScheduleService"/>. Every session is revoked and the auth cookies are cleared, as they
/// were when this deleted on the spot, so the response looks the same to a client except for the date in its body.
/// <para>
/// Gated by <see cref="VerifyUserPreProcessor{TUser,TRequest}"/> like the framework's
/// <c>BaseDeleteUserAccountEndpoint</c> this used to derive from: the current password and, where enabled, a 2FA code.
/// No longer derived from it because that base deletes, and here nothing is deleted until
/// <c>AccountDeletionJobHandler</c> runs, through <see cref="IUserAccountDeletionService"/>.
/// </para>
/// </summary>
public class DeleteUserAccountEndpoint(IAccountDeletionScheduleService deletionSchedule)
    : Endpoint<VerifyUserRequest, AccountDeletionScheduledResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Delete("user/account");
        Roles(AllowedRoles());
        PreProcessor<VerifyUserPreProcessor<User, VerifyUserRequest>>();
        Summary(s =>
        {
            s.Summary = "Schedule the deletion of the user account";
            s.Description = "The account and all its data are deleted once the grace period has passed, unless the " +
                            "user keeps it before then — by the mailed link, or by signing in on the web.";
        });
    }

    public override async Task HandleAsync(VerifyUserRequest req, CancellationToken ct)
    {
        // Verified by the pre-processor — use that instance rather than re-querying, so the account scheduled is
        // exactly the one whose identity was proven.
        var user = HttpContext.GetVerifiedUser<User>();

        var deletesAt = await deletionSchedule.ScheduleAsync(user, ct);

        // Every session was revoked, this one included; the cookies would only fail at the next request.
        HttpContext.Response.ClearSessionCookies();

        await Send.OkAsync(new AccountDeletionScheduledResponse(deletesAt), ct);
    }
}

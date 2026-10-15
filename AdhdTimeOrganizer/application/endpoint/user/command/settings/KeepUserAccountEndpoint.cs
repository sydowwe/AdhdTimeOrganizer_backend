using AdhdTimeOrganizer.application.service.user;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

/// <summary>
/// <c>POST /user/account/keep</c> — P3-07: cancels the pending deletion of the signed-in user's account. What the web
/// app calls when someone signs in during the grace period and answers "keep my account", or presses it in settings.
/// Idempotent: with nothing pending it is still a 204.
/// <para>
/// Deliberately not automatic on sign-in: the user may be signing in only to export their data before it goes.
/// </para>
/// </summary>
public class KeepUserAccountEndpoint(IAccountDeletionScheduleService deletionSchedule) : EndpointWithoutRequest
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/user/account/keep");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = "Keep the account: cancel its scheduled deletion";
            s.Response(204, "Kept, or no deletion was pending");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await deletionSchedule.KeepAsync(User.GetId(), ct);
        await Send.NoContentAsync(ct);
    }
}

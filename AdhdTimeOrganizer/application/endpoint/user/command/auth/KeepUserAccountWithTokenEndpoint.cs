using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.service.user;
using FastEndpoints;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

/// <summary>
/// <c>POST /auth/account/keep</c> — P3-07: the "keep my account" link from the deletion mail. Unauthenticated, because
/// scheduling revoked every session; the token is the proof, the way an email-change revert link's is.
/// <para>
/// One answer for every refusal — unknown user, wrong token, a link already used, a deletion already past — so the
/// response cannot be used to probe which accounts are scheduled. Throttled on the client IP like the other
/// anonymous token endpoints.
/// </para>
/// </summary>
public class KeepUserAccountWithTokenEndpoint(IAccountDeletionScheduleService deletionSchedule)
    : Endpoint<KeepUserAccountRequest>
{
    public override void Configure()
    {
        Post("/auth/account/keep");
        AllowAnonymous();
        Throttle(10, 60, TrustedIpMiddleware.ClientIpHeaderName);
        Summary(s =>
        {
            s.Summary = "Keep the account with the link from the deletion mail";
            s.Response(204, "Kept");
            s.Response(400, "The link is not valid: wrong, already used, or the account is already deleted");
        });
    }

    public override async Task HandleAsync(KeepUserAccountRequest req, CancellationToken ct)
    {
        if (!await deletionSchedule.KeepWithTokenAsync(req.UserId, req.Token, ct))
        {
            AddError("This link is no longer valid.");
            await Send.ErrorsAsync(ResultErrorType.BadRequest, 400, ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}

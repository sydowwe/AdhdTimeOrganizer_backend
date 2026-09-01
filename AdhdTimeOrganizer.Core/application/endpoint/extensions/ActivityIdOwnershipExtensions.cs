using AdhdTimeOrganizer.Core.application.dto.request.extendable;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using FastEndpoints;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Core.application.endpoint.extensions;

/// <summary>
/// The one-line ownership guard for every request carrying an <see cref="IActivityIdRequest"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IActivityIdRequest"/> is a bare id holder and every DTO built on it inherits the same
/// hole: the <c>Activity</c> FK enforces existence, not ownership, and the row being written is
/// stamped with the caller's own <c>UserId</c>, so a request naming a stranger's sequential id creates
/// a perfectly valid row of the caller's pointing at somebody else's activity. Nothing downstream can
/// tell — <c>BaseCreateEndpoint</c> / <c>BaseUpdateEndpoint</c> do not check, the validators see only
/// a number, and every read then loses the join to the global query filter instead of erroring.
/// </para>
/// <para>
/// Lives here rather than in a validator because the check needs the request scope's
/// <see cref="DbContext"/> and the principal; FluentValidation validators in this solution are
/// stateless rule declarations. Call it from <c>BeforeMapping</c> and return its result — <c>false</c>
/// means the response has already been sent.
/// </para>
/// </remarks>
public static class ActivityIdOwnershipExtensions
{
    /// <summary>
    /// Verifies <see cref="IActivityIdRequest.ActivityId"/> belongs to the caller, answering <b>404 on
    /// the <c>ActivityId</c> field</b> when it does not.
    /// </summary>
    /// <remarks>
    /// 404 rather than 403, and the same message for a missing id as for a foreign one: an endpoint
    /// that distinguishes them is an existence oracle over another account's ids. Archived activities
    /// pass — see <see cref="IReferencedIdOwnershipService.OwnsActivityAsync"/>.
    /// </remarks>
    /// <returns><c>true</c> to continue; <c>false</c> when the response has already been written.</returns>
    public static async Task<bool> EnsureOwnsActivityAsync<TRequest, TResponse>(
        this Endpoint<TRequest, TResponse> endpoint,
        IActivityIdRequest req,
        IReferencedIdOwnershipService ownership,
        DbContext db,
        CancellationToken ct)
        where TRequest : notnull
    {
        if (await ownership.OwnsActivityAsync(db, endpoint.User.GetId(), req.ActivityId, ct))
            return true;

        endpoint.ValidationFailures.Add(
            new ValidationFailure(nameof(IActivityIdRequest.ActivityId), "Activity not found."));
        await endpoint.HttpContext.Response.SendErrorsAsync(endpoint.ValidationFailures, 404, cancellation: ct);
        return false;
    }
}

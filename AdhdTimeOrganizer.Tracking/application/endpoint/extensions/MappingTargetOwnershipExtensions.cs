using AdhdTimeOrganizer.Core.domain.serviceContract;
using FastEndpoints;
using FluentValidation.Results;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.extensions;

/// <summary>
/// The ownership guard for the three target ids a pattern mapping carries — activity, role, category.
/// </summary>
/// <remarks>
/// <para>
/// Same hole as <c>ActivityIdOwnershipExtensions</c> and worse in consequence: a mapping is a
/// <b>standing rule</b>, not a single row. The create request writes the raw ids through, the FK is
/// satisfied by any existing row including a stranger's, and every later heartbeat matching that
/// pattern then attributes the caller's time against the other user's activity and publishes
/// <c>ActivityTimeRecordedEvent</c> with it, indefinitely. Read-back is query-filtered, so the user
/// sees a mapping that looks broken rather than another account's name — which is why it would never
/// be reported as a bug.
/// </para>
/// <para>
/// 404 per offending field, with the same message for a missing id as for a foreign one: telling the
/// two apart makes the endpoint an existence oracle over another account's ids. Call it from
/// <c>BeforeMapping</c> and return its result — <c>false</c> means the response is already written.
/// </para>
/// </remarks>
public static class MappingTargetOwnershipExtensions
{
    /// <returns><c>true</c> to continue; <c>false</c> when the response has already been written.</returns>
    public static async Task<bool> EnsureOwnsMappingTargetsAsync<TRequest>(
        this Endpoint<TRequest, long> endpoint,
        IReferencedIdOwnershipService ownership,
        DbContext db,
        long? activityId,
        long? roleId,
        long? categoryId,
        CancellationToken ct)
        where TRequest : notnull
    {
        var userId = endpoint.User.GetId();
        var failures = new List<ValidationFailure>();

        if (activityId is { } activity && !await ownership.OwnsActivityAsync(db, userId, activity, ct))
            failures.Add(new ValidationFailure("ActivityId", "Activity not found."));

        if (roleId is { } role && !await ownership.OwnsActivityRoleAsync(db, userId, role, ct))
            failures.Add(new ValidationFailure("RoleId", "Role not found."));

        if (categoryId is { } category && !await ownership.OwnsActivityCategoryAsync(db, userId, category, ct))
            failures.Add(new ValidationFailure("CategoryId", "Category not found."));

        if (failures.Count == 0)
            return true;

        endpoint.ValidationFailures.AddRange(failures);
        await endpoint.HttpContext.Response.SendErrorsAsync(endpoint.ValidationFailures, 404, cancellation: ct);
        return false;
    }
}

using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dto.request.history;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.command;

/// <summary>
/// The ownership check the create and update endpoints share: none of the three ids on
/// <see cref="ActivityHistoryRequest"/> is minted server-side, and none of them is checked by anything
/// else.
/// </summary>
/// <remarks>
/// <para>
/// The row's own <c>UserId</c> is stamped server-side, which is what makes this look safe and is not:
/// the record is created <em>owned by the caller</em> while pointing at another user's activity. The
/// <c>Activity</c> FK enforces existence, not ownership, and the two item links carry no navigation at
/// all. The damage is not theoretical — <c>Activity</c> is <c>IEntityWithUser</c>, so the global filter
/// applies to the <c>Include</c> on every read path too, the join comes back null, and
/// <c>ActivityHistoryResponse.Projection</c> materializes a non-nullable <c>Id</c> from it. One such
/// row makes the caller's own grid throw for that date group.
/// </para>
/// <para>
/// Lives beside the endpoints rather than in <c>ActivityHistoryValidator</c> because FluentValidation
/// validators here are stateless rule declarations resolved once, while this needs the request scope's
/// <see cref="DbContext"/> and the principal. It is the same reason
/// <c>ScheduleTimerAlarmsEndpoint</c> checks its own <c>ActivityId</c> in the handler.
/// </para>
/// <para>
/// Both endpoints check all three ids, including update — where <c>UpdateEntity</c> deliberately does
/// not write the two item links. Checking an id the update ignores costs one query on a request that
/// should not be carrying a foreign id in the first place, and the alternative (two different rules
/// for one DTO) is the kind of asymmetry that comes back as a hole the day update starts writing them.
/// </para>
/// </remarks>
internal static class ActivityHistoryReferencedIds
{
    /// <summary>
    /// The first referenced id <paramref name="userId"/> does not own, as a ready-to-report validation
    /// failure, or <c>null</c> when all three check out.
    /// </summary>
    /// <remarks>
    /// Short-circuits on the first failure and reports "not found" for both the missing and the foreign
    /// case, so the response never confirms that another account's id exists.
    /// </remarks>
    public static async Task<ValidationFailure?> FirstUnownedAsync(
        ActivityHistoryRequest req,
        IReferencedIdOwnershipService ownership,
        DbContext db,
        long userId,
        CancellationToken ct)
    {
        if (!await ownership.OwnsActivityAsync(db, userId, req.ActivityId, ct))
            return new ValidationFailure(nameof(ActivityHistoryRequest.ActivityId), "Activity not found.");

        if (req.TodoListItemId is { } todoListItemId
            && !await ownership.OwnsReferenceAsync(db, UserOwnedReferenceKeys.TodoListItem, userId, todoListItemId, ct))
            return new ValidationFailure(nameof(ActivityHistoryRequest.TodoListItemId), "To-do item not found.");

        if (req.RoutineTodoListId is { } routineTodoListId
            && !await ownership.OwnsReferenceAsync(db, UserOwnedReferenceKeys.RoutineTodoList, userId, routineTodoListId, ct))
            return new ValidationFailure(nameof(ActivityHistoryRequest.RoutineTodoListId), "Routine item not found.");

        return null;
    }
}

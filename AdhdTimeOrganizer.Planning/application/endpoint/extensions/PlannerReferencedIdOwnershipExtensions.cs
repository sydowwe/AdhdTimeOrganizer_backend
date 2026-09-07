using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;
using FluentValidation.Results;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.extensions;

/// <summary>
/// The ownership guard for the ids a planner write carries but does not mint — <c>ActivityId</c>,
/// <c>ImportanceId</c>, <c>TodolistId</c>, and the settings' <c>DefaultApplyTemplateId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Same hole as <c>ActivityIdOwnershipExtensions</c> and <c>MappingTargetOwnershipExtensions</c>: the FK
/// proves the row exists, never that it is the caller's, and the task being written is stamped with the
/// caller's own <c>UserId</c> — so a task naming a stranger's <c>ActivityId</c> inserts cleanly and looks
/// like any other. It is not invisible either. <c>PlannerTask.Color</c> reads through
/// <c>Activity.Role.Color</c> and <c>PlannerTaskResponse</c> projects the same navigation, so the victim's
/// role colour, name and category come back on every read of the attacker's own day.
/// </para>
/// <para>
/// <c>TodolistId</c> is the one that cannot be checked here directly: Planning cannot see TodoLists, the
/// column is the navigation-free FK the host declares, and the check therefore goes back out through Core's
/// <see cref="IReferencedIdOwnershipService"/> and the <see cref="IUserOwnedReferenceVerifier"/> seam —
/// which <b>fails closed</b>, so a slice dropped from <c>ModuleAssemblies</c> rejects these writes rather
/// than waving them through. The other two are Planning's own tables and are compared here, on the
/// <c>userId</c> passed in rather than on the ambient query filter: the filter answers "who is executing",
/// and it is <c>null</c> in any unauthenticated context.
/// </para>
/// <para>
/// 404 per offending field, with the same message for a missing id as for a foreign one — telling the two
/// apart makes the endpoint an existence oracle over another account's ids. Call it from
/// <c>BeforeMapping</c> and return its result; <c>false</c> means the response is already written.
/// </para>
/// </remarks>
public static class PlannerReferencedIdOwnershipExtensions
{
    /// <returns><c>true</c> to continue; <c>false</c> when the response has already been written.</returns>
    public static Task<bool> EnsureOwnsPlannerTaskReferencesAsync(
        this IEndpoint endpoint,
        DbContext db,
        IReferencedIdOwnershipService ownership,
        PlannerTaskRequest req,
        CancellationToken ct) =>
        endpoint.EnsureOwnsPlannerTaskReferencesAsync(db, ownership, [req], ct);

    /// <summary>
    /// The batched form, for the tasks an apply-template request builds. Ids are de-duplicated first: a
    /// template's lines commonly repeat one activity, and the check is one query per distinct id.
    /// </summary>
    /// <returns><c>true</c> to continue; <c>false</c> when the response has already been written.</returns>
    public static async Task<bool> EnsureOwnsPlannerTaskReferencesAsync(
        this IEndpoint endpoint,
        DbContext db,
        IReferencedIdOwnershipService ownership,
        IReadOnlyCollection<PlannerTaskRequest> reqs,
        CancellationToken ct)
    {
        var userId = endpoint.HttpContext.User.GetId();
        var failures = new List<ValidationFailure>();

        foreach (var activityId in reqs.Select(r => r.ActivityId).Distinct())
        {
            if (!await ownership.OwnsActivityAsync(db, userId, activityId, ct))
            {
                failures.Add(new ValidationFailure(nameof(PlannerTaskRequest.ActivityId), "Activity not found."));
                break;
            }
        }

        foreach (var importanceId in reqs.Select(r => r.ImportanceId).OfType<long>().Distinct())
        {
            if (!await db.Set<TaskImportance>().AnyAsync(i => i.Id == importanceId && i.UserId == userId, ct))
            {
                failures.Add(new ValidationFailure(nameof(PlannerTaskRequest.ImportanceId), "Importance not found."));
                break;
            }
        }

        foreach (var todolistItemId in reqs.Select(r => r.TodolistId).OfType<long>().Distinct())
        {
            if (!await ownership.OwnsReferenceAsync(db, UserOwnedReferenceKeys.TodoListItem, userId, todolistItemId, ct))
            {
                failures.Add(new ValidationFailure(nameof(PlannerTaskRequest.TodolistId), "To-do item not found."));
                break;
            }
        }

        return await endpoint.ReportAsync(failures, ct);
    }

    /// <summary>
    /// Verifies a day-template id the caller supplied is theirs. Used for
    /// <c>UserPlannerSettingsRequest.DefaultApplyTemplateId</c>, which is stored as a bare id and returned as
    /// one — an integrity defect rather than a leak, and still a stored pointer at a row the user cannot see.
    /// </summary>
    /// <returns><c>true</c> to continue; <c>false</c> when the response has already been written.</returns>
    public static async Task<bool> EnsureOwnsDayTemplateAsync(
        this IEndpoint endpoint,
        DbContext db,
        long templateId,
        string fieldName,
        CancellationToken ct)
    {
        var userId = endpoint.HttpContext.User.GetId();

        if (await db.Set<TaskPlannerDayTemplate>().AnyAsync(t => t.Id == templateId && t.UserId == userId, ct))
            return true;

        return await endpoint.ReportAsync([new ValidationFailure(fieldName, "Template not found.")], ct);
    }

    private static async Task<bool> ReportAsync(this IEndpoint endpoint, List<ValidationFailure> failures, CancellationToken ct)
    {
        if (failures.Count == 0)
            return true;

        endpoint.ValidationFailures.AddRange(failures);
        await endpoint.HttpContext.Response.SendErrorsAsync(endpoint.ValidationFailures, 404, cancellation: ct);
        return false;
    }
}

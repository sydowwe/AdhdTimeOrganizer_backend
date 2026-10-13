using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;
using FluentValidation.Results;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.domain.result;

namespace AdhdTimeOrganizer.Planning.application.endpoint.extensions;

/// <summary>
/// The endpoint half of <see cref="PlannerTaskLaneRule"/>: a 409 <c>Conflict</c> on <c>startTime</c> when a write
/// would put a third block of its kind at some minute it newly covers. 409 rather than 400 because the request is well formed —
/// it is the day it lands on that refuses it, and another block moving away would make the same request succeed.
/// </summary>
public static class PlannerTaskLaneExtensions
{
    /// <summary>
    /// Call from <c>AfterMapping</c>, once the task's day and span are final and before anything resets the
    /// tracker's original values (the newly covered minutes are read off them), and return its result.
    /// </summary>
    /// <returns><c>true</c> to continue; <c>false</c> when the response has already been written.</returns>
    public static async Task<bool> EnsureWithinLanesAsync(this IEndpoint endpoint, DbContext db, PlannerTask task, CancellationToken ct)
    {
        if (!await PlannerTaskLaneRule.WouldExceedAsync(db, task, ct))
            return true;

        endpoint.ValidationFailures.Add(new ValidationFailure("startTime", PlannerTaskLaneRule.ErrorMessage));
        await endpoint.HttpContext.Response.SendErrorsAsync(
            endpoint.ValidationFailures, endpoint.ErrorStatus(ResultErrorType.Conflict), cancellation: ct);
        return false;
    }
}

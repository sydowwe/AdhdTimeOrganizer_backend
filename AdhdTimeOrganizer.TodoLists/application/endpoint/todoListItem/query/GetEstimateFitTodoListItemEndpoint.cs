using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.rule;
using AdhdTimeOrganizer.TodoLists.application.dto.@enum;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.query;

/// <summary>
/// F3-01: how the user's to-do estimates compare with what happened, per list, category or priority — the logged
/// time against each item and the block it was first planned into, both as a ratio of <c>SuggestedTime</c>.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the activity-keyed calibration (todo-motivation S3), which divided an activity's whole history by its
/// <em>sittings</em> and compared that with a per-<em>item</em> estimate, so an item done in three sittings read as
/// a third of its length. Every join here is an exact item link instead: <c>ActivityHistory.TodoListItemId</c>
/// through <see cref="ITodoListItemLoggedTimeSource"/> and <c>PlannerTask.TodolistItemId</c> through
/// <see cref="ITodoListItemPlanSource"/>. Never widen the logged side to the activity — two items may share one.
/// </para>
/// <para>
/// A sample is an item completed in the last <see cref="WindowDays"/> days with an estimate and at least one
/// second logged against it. Logged time is taken across all days, not only the window: an item finished
/// yesterday may have been worked on last month. Groups below <see cref="EstimateFitRule.TodoItemGroupMinimumSamples"/>
/// are left out (<see cref="EstimateFitRule"/>). Computed on read: a user has hundreds of such items at most.
/// </para>
/// <para>
/// Logged time is optional, so the samples are the items the user chose to time. The response describes the
/// estimate and nothing else; it never rewrites <c>SuggestedTime</c>, which also decides tracker auto-completion.
/// </para>
/// </remarks>
public class GetEstimateFitTodoListItemEndpoint(
    DbContext db,
    ITodoListItemLoggedTimeSource loggedTime,
    ITodoListItemPlanSource planSource,
    TimeProvider time)
    : Endpoint<TodoListItemEstimateFitRequest, TodoListItemEstimateFitResponse>
{
    /// <summary>How far back a completion still counts. Older estimates say less about how the user estimates now.</summary>
    public const int WindowDays = 90;

    public override void Configure()
    {
        Get("/todo-list-item/estimate-fit");
        Roles(this.GetUserRole());
        Validator<TodoListItemEstimateFitValidator>();

        Summary(s =>
        {
            s.Summary = "How to-do estimates compared with logged and planned time, per list, category or priority";
            s.Response<TodoListItemEstimateFitResponse>(200, "Groups with enough items, and per-item logged time");
            s.Response(400, "groupBy missing or not one of list, category, priority");
        });
    }

    public override async Task HandleAsync(TodoListItemEstimateFitRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var now = time.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-WindowDays);

        // Filtered on UserId explicitly, like the recap: this row set is what the response is built from.
        var candidates = await db.Set<TodoListItem>()
            .AsNoTracking()
            .Where(i => i.UserId == userId
                        && i.SuggestedTime != null
                        && (!i.IsDone || (i.CompletedTimestamp != null && i.CompletedTimestamp >= windowStart)))
            .Select(i => new
            {
                i.Id,
                i.IsDone,
                i.SuggestedTime,
                i.TodoListId,
                CategoryId = i.TodoList != null ? i.TodoList.CategoryId : null,
                i.TaskPriorityId
            })
            .ToListAsync(ct);

        // SuggestedTime is an IntTime behind a converter, so "> 0" is read here rather than in SQL.
        var estimated = candidates.Where(c => c.SuggestedTime!.TotalSeconds > 0).ToList();
        if (estimated.Count == 0)
        {
            await Send.ResponseAsync(new TodoListItemEstimateFitResponse { Groups = [], Items = [] }, cancellation: ct);
            return;
        }

        var ids = estimated.Select(c => c.Id).ToList();

        // From the epoch to a day ahead: the item link only exists since 2026-10-03, so there is nothing older to
        // find, and a manual log may be dated later today than the request.
        var loggedByItem = await loggedTime.LoggedSecondsBetweenAsync(db, userId, ids, DateTime.UnixEpoch, now.AddDays(1), ct);

        var timed = estimated.Where(c => loggedByItem.GetValueOrDefault(c.Id) > 0).ToList();
        var samples = timed.Where(c => c.IsDone).ToList();

        var plannedByItem = await planSource.FirstPlannedSecondsAsync(db, userId, samples.Select(c => c.Id).ToList(), ct);

        var groups = samples
            .GroupBy(c => req.GroupBy switch
            {
                EstimateFitGroupBy.Category => c.CategoryId,
                EstimateFitGroupBy.Priority => (long?)c.TaskPriorityId,
                _ => c.TodoListId
            })
            .Select(g =>
            {
                var loggedRatios = g
                    .Select(c => (double)loggedByItem[c.Id] / c.SuggestedTime!.TotalSeconds)
                    .ToList();
                var plannedRatios = g
                    .Where(c => plannedByItem.ContainsKey(c.Id))
                    .Select(c => (double)plannedByItem[c.Id] / c.SuggestedTime!.TotalSeconds)
                    .ToList();

                var loggedRatio = EstimateFitRule.MedianOrSilent(loggedRatios, EstimateFitRule.TodoItemGroupMinimumSamples);
                return loggedRatio is null
                    ? null
                    : new EstimateFitGroup
                    {
                        Key = g.Key,
                        LoggedRatio = loggedRatio.Value,
                        SampleCount = loggedRatios.Count,
                        PlannedRatio = EstimateFitRule.MedianOrSilent(plannedRatios, EstimateFitRule.TodoItemGroupMinimumSamples),
                        PlannedSampleCount = plannedRatios.Count
                    };
            })
            .OfType<EstimateFitGroup>()
            .OrderByDescending(g => g.SampleCount)
            .ToList();

        var items = timed
            .Select(c => new EstimateFitItemLoggedTime { ItemId = c.Id, LoggedSeconds = loggedByItem[c.Id] })
            .ToList();

        await Send.ResponseAsync(new TodoListItemEstimateFitResponse { Groups = groups, Items = items }, cancellation: ct);
    }
}

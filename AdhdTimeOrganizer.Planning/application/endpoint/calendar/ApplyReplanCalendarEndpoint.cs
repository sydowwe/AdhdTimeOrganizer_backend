using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.dto.@enum;
using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.calendar;

/// <summary>
/// <c>POST /calendar/day-plan/{date}/replan/apply</c> — FO4: applies a re-plan the user has seen. Moves every block
/// the preview moved, resolves every block it had no room for (to tomorrow, or a skip with <c>noTime</c>), in one
/// save, then re-syncs the reminders of every touched task in one call.
/// <para>
/// <b>It does not trust the client's placements — it never receives any.</b> The client sends back the preview's
/// token, and the plan is <b>recomputed here</b> from the token's packed-from against the day as it now stands
/// (<see cref="DayReplanPlanner"/>). The recomputed token must equal the one sent: if any block on the day was
/// added, removed, moved, started, finished or re-flagged since the preview, it does not, and the answer is a 409
/// (<c>errorType: "Conflict"</c>) with nothing written. Recompute-and-compare rather than validating posted slots,
/// because the slots are a function of the day; letting the client name them would mean re-proving every rule the
/// packing already keeps (no overlap with a fixed block, length kept, inside the window, nothing earlier than its
/// plan) at a second site.
/// </para>
/// <para>
/// Three more ways it fails safely, none of which half-applies:
/// <list type="bullet">
/// <item>A preview older than <see cref="DayReplanPlanner.PreviewLifetime"/> is a 409 too: its first placement is
/// already well behind the user.</item>
/// <item>The tasks are loaded tracked, so a write that lands between the recompute and the save loses — or wins —
/// the <c>row_version</c> race on the rows both touch; the loser of the save gets a 409
/// (<c>DbConcurrencyError</c>) and the whole save rolls back, because it is one <c>SaveChanges</c>.</item>
/// <item>A double submit is the first case: once applied, the day no longer produces the preview's token.</item>
/// </list>
/// </para>
/// <para>
/// Every move — within today and to tomorrow — goes through <see cref="PlannerTaskMoveRecorder"/>, judged against
/// the request's one clock, so a re-plan counts postponements exactly as the same moves made by hand would. A move
/// to tomorrow is a postponement, not a carry-over: <c>CarryOverCount</c> is the carry command's alone. A skip
/// publishes no completion fan-out: a <c>NotStarted</c> block skipped was not done before and is not done after.
/// </para>
/// </summary>
public class ApplyReplanCalendarEndpoint(
    DbContext dbContext,
    IUserTimeZoneResolver timeZones,
    ICalendarProvisioner calendars,
    IReminderRegistrationService reminders,
    TimeProvider timeProvider)
    : Endpoint<ApplyDayReplanRequest, ApplyDayReplanResponse>
{
    /// <summary>
    /// How far ahead of the server's clock a token's packed-from may be. Packed-from is the preview's now rounded
    /// <i>up</i>, so a preview applied within its own minute is ahead by under one.
    /// </summary>
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(1);

    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/calendar/day-plan/{date}/replan/apply");
        Roles(AllowedRoles());
        Validator<ApplyDayReplanValidator>();
        Summary(s =>
        {
            s.Summary = "Apply a re-plan of the rest of today";
            s.Description =
                "Recomputes the plan from the preview's token against the day as it now stands and applies it in one "
                + "save: moves the blocks, moves each wontFit block to tomorrow (same time of day) or skips it with the "
                + "reason noTime, records postponements as a manual move would, and re-syncs reminders once. wontFit must "
                + "decide exactly the preview's wontFit tasks.";
            s.Response<ApplyDayReplanResponse>(200, "Applied — the ids of what changed");
            s.Response(400, "Malformed date or token, a date that is not the user's today, or wontFit decisions that do not match the preview's");
            s.Response(409, "The preview is stale — the day changed, it is older than ten minutes, or a concurrent write won the race. Nothing was written; ask for a new preview");
        });
    }

    public override async Task HandleAsync(ApplyDayReplanRequest req, CancellationToken ct)
    {
        if (!TaskPlannerHelper.TryParseDayPlanRouteDate(Route<string>("date"), out var date))
        {
            AddError("date", "Date must be in yyyy-MM-dd format.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var userId = User.GetId();
        var zone = await timeZones.GetAsync(userId, ct);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        if (date != TaskPlannerHelper.TodayIn(zone, utcNow))
        {
            AddError("date", "Only the user's today can be re-planned.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        if (!DayReplanPlanner.TryReadPackedFrom(req.PreviewToken, out var packedFrom))
        {
            AddError(r => r.PreviewToken, "Not a re-plan preview token.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        var previewedAt = DayReplanPlanner.InstantOf(date, packedFrom, zone);
        if (previewedAt < utcNow - DayReplanPlanner.PreviewLifetime || previewedAt > utcNow + ClockTolerance)
        {
            AddError(r => r.PreviewToken, "The preview has expired. Ask for a new one.");
            await Send.ErrorsAsync(this.ErrorStatus(ResultErrorType.Conflict), ct);
            return;
        }

        try
        {
            // Provisioning commits, so tomorrow is resolved before anything is loaded for writing. A 409 below leaves
            // the row behind, which is harmless: calendar presence never means "planned" (DayPlanResponse.HasPlan).
            Calendar? tomorrow = req.WontFit.Any(d => d.Resolution == ReplanWontFitResolution.Tomorrow)
                ? await calendars.EnsureForDateAsync(userId, date.AddDays(1), ct)
                : null;

            var (calendar, tasks) = await DayReplanPlanner.LoadDayAsync(dbContext, userId, date, track: true, ct);
            var plan = DayReplanPlanner.Compute(calendar, tasks, date, packedFrom);

            if (!string.Equals(plan.Token, req.PreviewToken, StringComparison.Ordinal))
            {
                AddError(r => r.PreviewToken, "The day has changed since the preview. Ask for a new one.");
                await Send.ErrorsAsync(this.ErrorStatus(ResultErrorType.Conflict), ct);
                return;
            }

            var expected = plan.WontFit.Select(w => w.TaskId).ToHashSet();
            if (!expected.SetEquals(req.WontFit.Select(d => d.TaskId)))
            {
                AddError(r => r.WontFit,
                    $"Decide every task that won't fit, and only those: [{string.Join(", ", plan.WontFit.Select(w => w.TaskId))}].");
                await Send.ErrorsAsync(cancellation: ct);
                return;
            }

            var byId = tasks.ToDictionary(t => t.Id);
            var moved = new List<long>();
            var movedToTomorrow = new List<long>();
            var skipped = new List<long>();

            foreach (var move in plan.Moves)
            {
                var task = byId[move.TaskId];
                task.StartTime = move.ToStart;
                task.EndTime = move.ToEnd;
                moved.Add(task.Id);
            }

            foreach (var decision in req.WontFit)
            {
                var task = byId[decision.TaskId];
                if (decision.Resolution == ReplanWontFitResolution.Tomorrow)
                {
                    task.CalendarId = tomorrow!.Id;
                    movedToTomorrow.Add(task.Id);
                }
                else
                {
                    task.ApplyStatus(PlannerTaskStatus.Cancelled, skipReasonKey: SkipReasonKey.NoTime);
                    skipped.Add(task.Id);
                }
            }

            // FO3: one rule decides whether each move was a postponement, against this request's one clock.
            foreach (var id in moved.Concat(movedToTomorrow))
                await PlannerTaskMoveRecorder.RecordPostponementAsync(dbContext, byId[id], timeZones, utcNow, ct);

            var touched = moved.Concat(movedToTomorrow).Concat(skipped).ToList();
            if (touched.Count > 0)
            {
                // One SaveChanges is one transaction: every block moves, or none does.
                await dbContext.SaveChangesAsync(ct);

                // Every touched task's reminder instant or status changed (Reminder.RemindAt gotcha) — one sync for
                // the whole set, after the commit.
                await reminders.SyncForPlannerTasksAsync(touched, ct);
            }

            await Send.OkAsync(new ApplyDayReplanResponse
            {
                MovedTaskIds = moved,
                MovedToTomorrowTaskIds = movedToTomorrow,
                SkippedTaskIds = skipped
            }, ct);
        }
        catch (Exception ex)
        {
            // The mapping every write endpoint uses: a lost row_version race is a 409, not a 500.
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
        }
    }
}

using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.application.service.routine;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using AdhdTimeOrganizer.Routines.infrastructure.persistence;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.query;

public class GetAllGroupedRoutineTodoListEndpoint(
    DbContext dbContext,
    IRoutinePeriodNotificationService notifier,
    IRoutineAwayDaysReader awayDaysReader,
    IRoutineItemLoggedTimeSource loggedTime) : EndpointWithoutRequest<IEnumerable<RoutineTodoListGroupedResponse>>
{
    public override void Configure()
    {
        Get("/routine-todo-list/grouped-by-time-period");

        Summary(s =>
        {
            s.Summary = "Get all routine todo lists grouped by time period";
            s.Description = "Retrieves all routine todo lists grouped by their time period";
            s.Response<IEnumerable<RoutineTodoListGroupedResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var loggedUserId = User.GetId();
        var now = DateTime.UtcNow;

        var periods = await dbContext.Set<RoutineTimePeriod>()
            .Where(x => x.UserId == loggedUserId)
            .Include(tp => tp.RoutineTodoListColl)
            .ThenInclude(rtl => rtl.Activity)
            .ThenInclude(a => a.Role)
            .Include(tp => tp.RoutineTodoListColl)
            .ThenInclude(rtl => rtl.Activity)
            .ThenInclude(a => a.Category)
            .Include(tp => tp.RoutineTodoListColl)
            .ThenInclude(rtl => rtl.Steps)
            .ToListAsync(ct);

        var changed = false;
        var newCompletions = new List<RoutinePeriodCompletion>();
        var reset = new List<(RoutineTimePeriod Period, RoutineResetService.RoutinePeriodReset Result)>();
        // Read only when something is due. Opening the app after a holiday is exactly when this read closes the
        // cycles the holiday covered, so it must excuse them here rather than leave it to the nightly job.
        var awayDays = (await awayDaysReader.ForDueCyclesAsync(dbContext, periods, now, ct)).GetValueOrDefault(loggedUserId);
        foreach (var period in periods)
        {
            var items = period.RoutineTodoListColl.ToList();
            changed |= RoutineResetService.CheckGrace(period, now);
            // No job refills the freeze budget — the refill is lazy, so the surface that renders the budget is
            // the one that has to bring it current. Skipping it here would serve a stale count and refuse a
            // freeze the user is owed.
            changed |= RoutineStreakFreezeService.RefreshFreezeBudget(period, now);
            // Catch-up, not a single cycle: opening the app after a fortnight away must leave the period current,
            // not one cycle less stale than it was.
            var result = RoutineResetService.TryResetCatchUp(period, items, now, awayDays);
            if (result is { } r)
            {
                changed = true;
                newCompletions.AddRange(r.Completions);
                reset.Add((period, r.Latest));
            }
        }

        if (changed)
        {
            dbContext.Set<RoutinePeriodCompletion>().AddRange(newCompletions);
            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (RoutineResetConflict.IsLostRace(ex))
            {
                // A second tab, or the nightly sweep mid-flight, closed the same cycle a moment earlier. Drop
                // our copy rather than 500 on the module's most-hit screen: the winner wrote the same values
                // (see RoutineResetConflict), so the in-memory periods rendered below still match the row.
                dbContext.ChangeTracker.Clear();
                // And say nothing — the winner is the one that owes the end-of-period summary, and two
                // requests announcing the same reset is the same bug pointed the other way.
                reset.Clear();
            }
        }

        // This read applies resets lazily, so it is a real reset site and owes the same summary the nightly job
        // sends — whichever path reaches the elapsed period first is the one that announces it. Raised after the
        // commit and best-effort, so it cannot fail the request the user is waiting on.
        foreach (var (period, result) in reset)
            await notifier.NotifyPeriodEndedAsync(period, result.Completion, result.Outcome, ct);

        // Only the newest HistoryDepth rows per period, trimmed by the database rather than after the fact: the
        // history grows one row per cycle forever, so fetching it whole to render 16 rows scales with account
        // age on the module's most-hit read. Take is a constant per query, so the periods are grouped by depth —
        // in practice one or two groups — and each group runs one LATERAL-per-period query.
        var completionsByPeriod = new Dictionary<long, List<RoutinePeriodCompletion>>();
        foreach (var depthGroup in periods.GroupBy(p => p.HistoryDepth))
        {
            var depth = depthGroup.Key;
            var periodIds = depthGroup.Select(p => p.Id).ToList();

            var completions = await dbContext.Set<RoutineTimePeriod>()
                .Where(p => periodIds.Contains(p.Id))
                .SelectMany(p => p.CompletionHistoryColl
                    .OrderByDescending(c => c.PeriodStart)
                    .Take(depth))
                .ToListAsync(ct);

            // The per-period order the subquery applied does not survive the join, so re-sort here — oldest
            // first, which is what RoutineTimePeriodResponse.From documents it takes.
            foreach (var g in completions.GroupBy(c => c.TimePeriodId))
                completionsByPeriod[g.Key] = g.OrderBy(c => c.PeriodStart).ToList();
        }

        // F2-06: each item's typical occurrence, over every period's items in one pass (two queries in all).
        var rendered = periods
            .SelectMany(tp => tp.RoutineTodoListColl)
            .Select(RoutineTodoListResponse.From)
            .ToList();
        var itemsById = (await RoutineItemTypicalDurationFiller.FillAsync(
                rendered, periods, dbContext, loggedTime, loggedUserId, now, ct))
            .ToDictionary(i => i.Id);

        var data = periods
            .Select(tp => new RoutineTodoListGroupedResponse
            {
                RoutineTimePeriod = RoutineTimePeriodResponse.From(
                    tp,
                    completionsByPeriod.GetValueOrDefault(tp.Id, []),
                    now),
                Items = tp.RoutineTodoListColl
                    .OrderBy(e => e.IsDone).ThenBy(e => e.DisplayOrder)
                    .Select(e => itemsById[e.Id])
                    .ToList()
            })
            .ToList();

        await Send.OkAsync(data, ct);
    }
}
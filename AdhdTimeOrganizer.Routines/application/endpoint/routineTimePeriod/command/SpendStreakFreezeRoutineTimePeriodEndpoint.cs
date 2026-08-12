using AdhdTimeOrganizer.Routines.application.dto.request.todoList;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.application.validator;
using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTimePeriod.command;

/// <summary>
/// Spends one streak freeze on an elapsed period that fell short of its threshold, so the run carries across the
/// miss instead of breaking.
/// <para>
/// Retroactive by design: the freeze is spent on a period that has already been scored, because requiring the
/// user to arm it beforehand would ask for exactly the foresight the streak is meant to reward the absence of.
/// The client only ever offers it on the newest miss, but any unfrozen miss still in the history is spendable —
/// covering an older one simply may not move the streak, since only the trailing run counts.
/// </para>
/// <para>
/// Returns the whole period, the same shape <c>GET /routine-todo-list/grouped-by-time-period</c> serves, so the
/// client re-reads the recomputed streak, the decremented budget and the newly frozen history entry through one
/// parser rather than patching its own state.
/// </para>
/// </summary>
public class SpendStreakFreezeRoutineTimePeriodEndpoint(DbContext dbContext)
    : Endpoint<SpendStreakFreezeRequest, RoutineTimePeriodResponse>
{
    public override void Configure()
    {
        Post("/routine-todo-list/time-period/{timePeriodId:long:required}/streak-freeze");
        Validator<SpendStreakFreezeValidator>();

        Summary(s =>
        {
            s.Summary = "Spend a streak freeze on a missed period";
            s.Description =
                "Covers one elapsed period that fell below the streak threshold, preserving the streak across it. " +
                "Returns the updated time period with the recomputed streak, the decremented freeze budget and " +
                "the covered completion entry flagged as frozen.";
            s.Response<RoutineTimePeriodResponse>(200, "Freeze spent");
            s.Response(400, "No freezes remaining, period already frozen, or nothing to cover");
            s.Response(404, "Time period or completion entry not found");
        });
    }

    public override async Task HandleAsync(SpendStreakFreezeRequest req, CancellationToken ct)
    {
        var timePeriodId = Route<long>("timePeriodId");
        var loggedUserId = User.GetId();
        var now = DateTime.UtcNow;

        // The global user filter already scopes this, but the explicit predicate is what makes the 404 an
        // ownership answer rather than an accident of whichever filter happens to be installed.
        var period = await dbContext.Set<RoutineTimePeriod>()
            .FirstOrDefaultAsync(x => x.Id == timePeriodId && x.UserId == loggedUserId, ct);

        if (period == null)
        {
            AddError("RoutineTimePeriod not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        // Validated, so this cannot fail — but the parse owns the conversion and the endpoint should not
        // duplicate it.
        if (!req.TryGetPeriodStart(out var periodStart))
        {
            AddError(r => r.PeriodStart, "PeriodStart must be an ISO-8601 date or instant.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // The whole history, not HistoryDepth rows: the streak is the length of the trailing run, and a run can
        // reach back past the window the client renders. Trimming here would silently shorten it.
        var history = await dbContext.Set<RoutinePeriodCompletion>()
            .Where(c => c.TimePeriodId == period.Id)
            .OrderBy(c => c.PeriodStart)
            .ToListAsync(ct);

        var entry = history.FirstOrDefault(c => c.PeriodStart == periodStart);
        if (entry == null)
        {
            AddError(r => r.PeriodStart, "No completion history entry starts on that date.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var rejection = RoutineStreakFreezeService.Apply(period, history, entry, now);
        if (rejection != StreakFreezeRejection.None)
        {
            AddError(RejectionMessage(rejection));
            await Send.ErrorsAsync(400, ct);
            return;
        }

        await dbContext.SaveChangesAsync(ct);

        await Send.OkAsync(
            RoutineTimePeriodResponse.From(period, history.TakeLast(period.HistoryDepth), now),
            ct);
    }

    private static string RejectionMessage(StreakFreezeRejection rejection) => rejection switch
    {
        StreakFreezeRejection.NoBudget => "No streak freezes remaining for this period.",
        StreakFreezeRejection.AlreadyFrozen => "That period is already covered by a streak freeze.",
        StreakFreezeRejection.NotAMiss => "That period met its streak threshold, so there is nothing to cover.",
        _ => "The streak freeze could not be spent."
    };
}

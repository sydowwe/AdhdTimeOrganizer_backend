using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

/// <summary>
/// Counts what deleting the account would destroy, for the SPA's danger-zone warning card.
///
/// <para><b>Cold path.</b> The client asks for this once, when the user actually opens the card — not on
/// every settings load. Latency is irrelevant; being cheap and being right are not.</para>
///
/// <para>It lives in the host and not in a slice because there is nowhere else it could: the answer spans
/// Core, History, Tracking, Planning, TodoLists, Routines and ActivityProfiles, and no slice can see another.
/// Adding a seam per slice to invert that would be six interfaces and six registrations for a read that
/// nothing but this card will ever perform — and every one of them would be a string-keyed resolution that
/// fails silently when a slice stops registering. The host already references all seven and already owns
/// <c>AppDbContext</c>; a slice extracted later takes its lines here with it.</para>
/// </summary>
public class GetAccountDeletionSummaryEndpoint(AppDbContext dbContext) : EndpointWithoutRequest<AccountDeletionSummaryResponse>
{
    public override void Configure()
    {
        Get("/user/account-deletion-summary");
        Summary(s =>
        {
            s.Summary = "Counts of the data that permanently deleting this account would destroy";
            s.Description =
                "Powers the deletion warning card. Read outside a transaction and intended as a warning, " +
                "not a reconciliation receipt. Cold path — request it when the card opens, not on every page load.";
            s.Response<AccountDeletionSummaryResponse>(200, "Success");
            s.Response(401, "Unauthorized");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        // One round trip, not fifteen. Every count below is a correlated scalar subquery in a single
        // projection off the user row, so Postgres plans them together and the endpoint pays one network
        // hop -- the reason a per-module fan-out from the client was rejected in the first place.
        //
        // Exact COUNT(*), not an estimate: each of these is an index range scan over one user's rows on an
        // index that already exists for the feature that owns the table (the (user_id, ...) leading edge is
        // universal here). An approximation would read reltuples for the *whole* table, which is both
        // wrong per-user and no cheaper. So the answer being exact is a side effect of the cheap plan, not
        // a cost paid for accuracy -- though it is still only exact as of this read; see the DTO.
        //
        // IgnoreQueryFilters is load-bearing and not a shortcut. WebExtensionActivityEntry carries a global
        // filter pinning reads to AppDbContext.CurrentPartitionDate (two years back); counting through it
        // would understate -- by an unbounded margin -- how much tracking data the delete actually destroys,
        // which is the one number this card exists to be honest about. Every set is therefore scoped to the
        // caller by hand below; nothing here leans on a filter. There are no soft-delete filters in this
        // model, so nothing else is widened by dropping them.
        var raw = await dbContext.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Timezone,
                GoogleCalendarLinked = u.GoogleCalendarRefreshToken != null,

                ActivityCount = dbContext.Activities.Count(a => a.UserId == userId),

                TrackedSessionCount = dbContext.ActivityHistories.Count(h => h.UserId == userId),
                // Served by the (user_id, start_timestamp) index ActivityHistoryConfiguration declares for
                // the history dashboards -- both aggregates are an index edge read, not a scan.
                FirstTrackedUtc = dbContext.ActivityHistories
                    .Where(h => h.UserId == userId)
                    .Min(h => (DateTime?)h.StartTimestamp),
                LastTrackedUtc = dbContext.ActivityHistories
                    .Where(h => h.UserId == userId)
                    .Max(h => (DateTime?)h.StartTimestamp),

                DesktopEntryCount = dbContext.DesktopActivityEntries.Count(e => e.UserId == userId),
                WebEntryCount = dbContext.WebExtensionActivityEntries.Count(e => e.UserId == userId),
                AndroidEntryCount = dbContext.AndroidSessionDataEntries.Count(e => e.UserId == userId),

                DayPlanCount = dbContext.Calendars.Count(c => c.UserId == userId),
                PlannerTaskCount = dbContext.PlannerTasks.Count(t => t.UserId == userId),
                DayTemplateCount = dbContext.TaskPlannerDayTemplates.Count(t => t.UserId == userId),

                TodoListCount = dbContext.TodoLists.Count(l => l.UserId == userId),
                TodoItemCount = dbContext.TodoListItems.Count(i => i.UserId == userId),
                RoutineCount = dbContext.RoutineTodoLists.Count(r => r.UserId == userId),

                // The three Activity*Profile rows are not IEntityWithUser -- they have no UserId column at
                // all -- so ownership is reached through the Activity, exactly as the three profile grids do.
                // MemoryAnchor does carry one, and is counted directly.
                BucketListCount = dbContext.Set<ActivityBucketListProfile>().Count(p => p.Activity.UserId == userId),
                ProjectCount = dbContext.Set<ActivityProjectProfile>().Count(p => p.Activity.UserId == userId),
                BacklogCount = dbContext.Set<ActivityBacklogProfile>().Count(p => p.Activity.UserId == userId),
                MemoryAnchorCount = dbContext.Set<MemoryAnchor>().Count(m => m.UserId == userId)
            })
            .FirstOrDefaultAsync(ct);

        if (raw is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        // The span is a count of the user's own calendar days, so both ends are read on the user's clocks --
        // WallClockZone.FromUtc, for the reason the data export gives: taken straight off the UTC instant, a
        // session recorded at 23:30 in Bratislava dates to the following day and a 00:30 one in Los Angeles
        // to the previous, which can shift the headline figure by a day at each end.
        var from = ToUserDate(raw.FirstTrackedUtc, raw.Timezone);
        var to = ToUserDate(raw.LastTrackedUtc, raw.Timezone);

        // Inclusive of both ends: one session yesterday and one today is two days of history, and a single
        // session is one -- not zero, which would read as "no history" beside a non-zero session count.
        int? spanDays = from is { } f && to is { } t ? t.DayNumber - f.DayNumber + 1 : null;

        await Send.OkAsync(new AccountDeletionSummaryResponse(
            ActivityCount: raw.ActivityCount,
            TrackedSessionCount: raw.TrackedSessionCount,
            TrackedFrom: from,
            TrackedTo: to,
            TrackedTimeSpanDays: spanDays,
            AutomaticTrackingEntryCount: raw.DesktopEntryCount + raw.WebEntryCount + raw.AndroidEntryCount,
            DayPlanCount: raw.DayPlanCount,
            PlannerTaskCount: raw.PlannerTaskCount,
            DayTemplateCount: raw.DayTemplateCount,
            TodoListCount: raw.TodoListCount,
            TodoItemCount: raw.TodoItemCount,
            RoutineCount: raw.RoutineCount,
            LeisureItemCount: raw.BucketListCount + raw.ProjectCount + raw.BacklogCount,
            MemoryAnchorCount: raw.MemoryAnchorCount,
            GoogleCalendarLinked: raw.GoogleCalendarLinked), ct);
    }

    private static DateOnly? ToUserDate(DateTime? instantUtc, TimeZoneInfo timeZone) =>
        instantUtc is { } instant ? DateOnly.FromDateTime(WallClockZone.FromUtc(instant, timeZone)) : null;
}

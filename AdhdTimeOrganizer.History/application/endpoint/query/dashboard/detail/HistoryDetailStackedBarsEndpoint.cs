using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dashboard;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.detail;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query.dashboard.detail;

public class HistoryDetailStackedBarsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<HistoryDetailStackedBarsRequest, HistoryStackedBarsResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/detail/stacked-bars");
    }

    public override async Task HandleAsync(HistoryDetailStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var timeZone = await timeZones.GetAsync(userId, ct);
        var (from, to) = req.ToDateTimeRange(timeZone);

        var records = await db.Set<ActivityHistory>()
            .AsNoTracking()
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);


        var windows = GenerateWindows(req.Date, req.From.ToTimeOnly(), to, timeZone);

        var response = new HistoryStackedBarsResponse
        {
            Windows = windows.Select(w => new HistoryWindow
            {
                WindowStart = w.Start,
                WindowEnd = w.End,
                Items = records
                    .Where(ah => ah.StartTimestamp >= w.Start && ah.StartTimestamp < w.End)
                    .GroupBy(ah => ah.ResolveGroupKey(req.GroupBy))
                    .Select(g => new HistoryGroupItem
                    {
                        GroupId = g.Key.Id,
                        Name = g.Key.Name,
                        TotalSeconds = g.Sum(ah => ah.Length.TotalSeconds),
                        Color = g.Key.Color
                    })
                    .OrderByDescending(i => i.TotalSeconds)
                    .ToList()
            }).ToList()
        };

        await Send.ResponseAsync(response, cancellation: ct);
    }

    /// <summary>
    /// One-hour bands across the window the user picked, clipped at its end — not a fixed 24, which put
    /// sixteen always-empty bars outside a 09:00–17:00 request and ran the last of them into the next day.
    ///
    /// <para>The walk is over the user's <b>wall clock</b> and every boundary is resolved back through the
    /// zone, for the reason the summary sibling's <c>GenerateFixedWindows</c> spells out: adding hours to a
    /// UTC instant slides every bar by the offset change on a DST transition day. <paramref name="to"/> is
    /// the instant <see cref="DateAndTimeRangeDto.ToDateTimeRange"/> already resolved, over-midnight rule
    /// included, so this needs no second reading of what the picked window means.</para>
    /// </summary>
    private static List<(DateTime Start, DateTime End)> GenerateWindows(
        DateOnly date, TimeOnly startTime, DateTime to, TimeZoneInfo timeZone)
    {
        var windows = new List<(DateTime Start, DateTime End)>();

        var wallClock = date.ToDateTime(startTime);
        var current = WallClockZone.ToUtc(wallClock, timeZone);

        while (current < to)
        {
            wallClock = wallClock.AddHours(1);

            var next = WallClockZone.ToUtc(wallClock, timeZone);
            if (next > to)
                next = to;

            // A band the spring-forward gap swallowed names no instants at all — both its boundaries
            // resolve to the moment the gap closes — so it is dropped rather than emitted as an empty bar.
            if (next > current)
                windows.Add((current, next));

            current = next;
        }

        return windows;
    }
}
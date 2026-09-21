using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.endpoint.extensions;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper.mapping;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.security;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.command;

/// <summary>
/// Ingests a desktop tracking window: persists the raw entries, and — for entries a pattern mapping
/// resolves to an activity — attributes the active seconds to that activity's time ledger.
/// </summary>
/// <remarks>
/// ⚠ <b>Ingest only. This endpoint must not write into another slice.</b> It used to do exactly that:
/// it wrote <c>ActivityHistory</c> rows inline (History), transitioned <c>PlannerTask.Status</c>
/// (Planning) and ticked off to-do and routine items (TodoLists, Routines) — three write-direction
/// dependencies, which is what kept Tracking from being extracted at all. Both are now seams owned by
/// Core:
/// <list type="bullet">
/// <item><see cref="IActivityTimeAttributionSink"/> takes the attribution write (History implements it).</item>
/// <item><see cref="ActivityTimeRecordedEvent"/> carries the day totals to whoever automates completion.</item>
/// </list>
/// Restoring either direct write re-couples Tracking to three slices; it will compile only for as long
/// as this file lives in the host.
/// </remarks>
[AllowExtensionClients]
public class DesktopActivityHeartbeatEndpoint(DbContext dbContext, IActivityTimeAttributionSink attributionSink)
    : Endpoint<DesktopActivityWindowDto>
{
    public override void Configure()
    {
        Post("/heartbeat");
        Summary(s =>
        {
            s.Summary = "Process desktop activity heartbeat";
            s.Description = "Records desktop activity data from client, updates activity history, and automates planner task/todo completion based on tracked time";
            s.Response<int>(200, "Success - returns count of processed entries");
            s.Response(400, "Bad request");
        });
        Validator<DesktopActivityHeartbeatValidator>();
        Group<ActivityTrackingDesktopGroup>();
        Policies(PortalAuthorizationPolicies.ActivityTracking);
    }

    public override async Task HandleAsync(DesktopActivityWindowDto req, CancellationToken ct)
    {
        var userId = User.GetId();
        var processedCount = 0;
        var attributedActivityIds = new HashSet<long>();

        // The one matcher: active rules, specificity order, first match wins (CQ-16). The distinct-entries
        // grid resolves its coveredBy chip through the same class, so what it shows is what happens here.
        var rules = new DesktopMappingRuleSet(await DesktopMappingRuleSet.ActiveFor(dbContext, userId).ToListAsync(ct));

        // CQ-8: what this window already holds. UserId, WindowStart and the derived RecordDate are fixed
        // for the request, so (ProcessName, WindowTitle) is the rest of the unique index — an agent
        // re-posting a window that did land gets a 200 with those entries skipped rather than a 23505 it
        // then retries identically forever. Skipping covers the attribution below with the entry: the
        // first attempt already attributed those seconds, and re-running the sink would count them twice.
        var seenKeys = (await dbContext.Set<DesktopActivityEntry>()
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.WindowStart == req.WindowStart)
                .Select(x => new { x.ProcessName, x.WindowTitle })
                .ToListAsync(ct))
            .Select(x => (x.ProcessName, x.WindowTitle))
            .ToHashSet();

        foreach (var entry in CollapseCollidingEntries(req.Entries))
        {
            // Seeded from the stored rows and grown as this batch is written, exactly as AndroidSyncEndpoint
            // keys its own dedup. CollapseCollidingEntries has already folded the in-batch repeats, so what
            // this rejects is a row a previous attempt committed.
            if (!seenKeys.Add((entry.ProcessName, entry.WindowTitle)))
                continue;

            var match = rules.Resolve(entry.ProcessName, entry.ProductName, entry.WindowTitle);

            if (match?.IsIgnored == true)
                continue;

            var record = new DesktopActivityEntry
            {
                UserId = userId,
                WindowStart = req.WindowStart,
                ProcessName = entry.ProcessName,
                ProductName = entry.ProductName,
                WindowTitle = entry.WindowTitle,
                ExecutablePath = entry.ExecutablePath,
                IsFullscreen = entry.IsFullscreen,
                ActiveSeconds = entry.ActiveSeconds,
                BackgroundSeconds = entry.BackgroundSeconds,
                IsPlayingSound = entry.IsPlayingSound,
                ActiveMonitor = entry.ActiveMonitor
            };

            dbContext.Set<DesktopActivityEntry>().Add(record);
            processedCount++;

            if (match?.ActivityId is { } activityId)
            {
                attributedActivityIds.Add(activityId);
                await attributionSink.AttributeAsync(dbContext, userId, activityId, req.WindowStart, entry.ActiveSeconds, ct);
            }
        }

        // One save for the raw entries and their attribution together, which is the sink's contract —
        // an implementation that saved on its own could leave entries recorded but unattributed.
        if (!await this.TrySaveIngestBatchAsync(dbContext, ct))
            return;

        await PublishAttributionAsync(userId, req.WindowStart, attributedActivityIds, ct);

        await Send.ResponseAsync(processedCount, cancellation: ct);
    }

    /// <summary>
    /// Folds the entries that would collide on <c>DesktopActivityEntry</c>'s unique index into one row each,
    /// summing the seconds — which is what the ledger means by two rows for the same window (CQ-8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index is <c>(UserId, WindowStart, RecordDate, ProcessName, WindowTitle)</c> and every entry in a
    /// heartbeat shares the first three, so two entries naming the same process and the same window title
    /// collide — which is exactly what one application's two windows on two monitors look like.
    /// <c>ActiveMonitor</c>, the field that tells them apart and the reason
    /// <c>DesktopProcessDetailsEndpoint</c> can report a per-monitor breakdown, is not in the key. Before
    /// this the whole window died with a 500 and the agent re-posted it unchanged.
    /// </para>
    /// <para>
    /// Grouping rather than widening the index: the index is on a partitioned table, so changing it is
    /// rollout work (<c>MIG-2</c>), and a second row for the same window is not information the dashboards
    /// have anywhere to put.
    /// </para>
    /// <para>
    /// Only the two second counts are combined. Everything else — including <c>ActiveMonitor</c> and
    /// <c>ProductName</c>, which the pattern mappings match on but the index does not carry — is taken from
    /// the busiest of the collided entries, so the row describes where the time actually went. A collision
    /// whose members disagree about <c>ProductName</c> is pathological (same executable, same window title),
    /// and the alternative to picking one is a row that cannot be stored at all.
    /// </para>
    /// </remarks>
    private static IEnumerable<DesktopActivityEntryDto> CollapseCollidingEntries(List<DesktopActivityEntryDto> entries) =>
        entries
            .Where(e => e.ActiveSeconds != 0 || e.BackgroundSeconds != 0)
            .GroupBy(e => (e.ProcessName, e.WindowTitle))
            .Select(group => group.Count() == 1
                ? group.First()
                : group.OrderByDescending(e => e.ActiveSeconds).First() with
                {
                    ActiveSeconds = group.Sum(e => e.ActiveSeconds),
                    BackgroundSeconds = group.Sum(e => e.BackgroundSeconds)
                });

    /// <summary>
    /// Announces the day totals for every activity this batch touched. Read <em>after</em> the save, so
    /// the batch's own seconds are included, and expressed as whole-day totals rather than the batch
    /// delta because every completion threshold downstream is cumulative.
    /// </summary>
    private async Task PublishAttributionAsync(long userId, DateTime windowStart, HashSet<long> attributedActivityIds, CancellationToken ct)
    {
        if (attributedActivityIds.Count == 0)
            return;

        var day = DateOnly.FromDateTime(windowStart);

        var totals = new List<ActivityDayTotal>(attributedActivityIds.Count);
        foreach (var activityId in attributedActivityIds)
            totals.Add(new ActivityDayTotal(
                activityId,
                await attributionSink.TotalSecondsOnDayAsync(dbContext, userId, activityId, day, ct)));

        await new ActivityTimeRecordedEvent(userId, day, totals).PublishAsync(Mode.WaitForAll, ct);
    }
}
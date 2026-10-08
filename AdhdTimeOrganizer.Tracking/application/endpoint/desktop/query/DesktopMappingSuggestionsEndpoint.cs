using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper.mapping;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.query;

/// <summary>
/// <c>GET /activity-tracking/desktop/mapping-suggestions?days=30</c> — which uncovered processes the user's own
/// timer focus phases point at one activity (FO9, F3-02). Feeds the "Suggested: Thesis" chip on the distinct-entries
/// grid's <i>Uncovered</i> rows; accepting it is the ordinary rule create, so nothing here writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The join is time overlap:</b> every desktop ledger minute whose <c>WindowStart</c> falls inside a focus
/// interval, one indexed <c>(UserId, WindowStart)</c> slice per interval. Minutes any active rule already covers —
/// ignore rules included — are dropped through <see cref="DesktopMappingRuleSet"/>, the one matcher the heartbeat and
/// the grid share, so a suggestion is never offered for a row the grid shows as decided. The thresholds live on
/// <see cref="MappingSuggestionRule"/>.
/// </para>
/// <para>
/// Only timer focus phases are read (<see cref="IFocusIntervalSource"/>); tracker-attributed history would teach the
/// rules from their own output.
/// </para>
/// </remarks>
public class DesktopMappingSuggestionsEndpoint(DbContext dbContext, IFocusIntervalSource focusIntervals)
    : Endpoint<DesktopMappingSuggestionsRequest, List<DesktopMappingSuggestionResponse>>
{
    /// <summary>
    /// The newest intervals read, at most. Each is one small indexed query; a heavy timer user's ninety days stay
    /// well under this, and past it the oldest sittings are the least telling anyway.
    /// </summary>
    private const int MaxIntervals = 500;

    public override void Configure()
    {
        Get("/mapping-suggestions");
        Summary(s =>
        {
            s.Summary = "Suggest desktop mapping rules from timer sessions";
            s.Description = "Uncovered processes that the user's timer focus phases attribute to one activity";
            s.Response<List<DesktopMappingSuggestionResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<DesktopMappingSuggestionsValidator>();
        Group<ActivityTrackingDesktopGroup>();
    }

    public override async Task HandleAsync(DesktopMappingSuggestionsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-req.Days);

        var intervals = (await focusIntervals.GetFocusIntervalsAsync(dbContext, userId, fromUtc, toUtc, ct))
            .TakeLast(MaxIntervals)
            .ToList();
        if (intervals.Count == 0)
        {
            await Send.OkAsync([], ct);
            return;
        }

        var rules = new DesktopMappingRuleSet(await DesktopMappingRuleSet.ActiveFor(dbContext, userId).AsNoTracking().ToListAsync(ct));
        var observations = new List<MappingSuggestionRule.Observation>();
        foreach (var interval in intervals)
        {
            var start = DateTime.SpecifyKind(interval.StartUtc, DateTimeKind.Utc);
            var end = DateTime.SpecifyKind(interval.EndUtc, DateTimeKind.Utc);
            var entries = await dbContext.Set<DesktopActivityEntry>()
                .AsNoTracking()
                .Where(e => e.UserId == userId && e.WindowStart >= start && e.WindowStart < end && e.ActiveSeconds > 0)
                .Select(e => new { e.ProcessName, e.ProductName, e.WindowTitle, e.ActiveSeconds })
                .ToListAsync(ct);

            observations.AddRange(entries
                .Where(e => rules.Resolve(e.ProcessName, e.ProductName, e.WindowTitle) is null)
                .Select(e => new MappingSuggestionRule.Observation(
                    e.ProcessName, e.ProductName, interval.ActivityId, interval.SessionId, e.ActiveSeconds)));
        }

        var suggestions = MappingSuggestionRule.Suggest(observations);
        if (suggestions.Count == 0)
        {
            await Send.OkAsync([], ct);
            return;
        }

        // Through the ambient query filters on purpose: an archived activity is not one to suggest a rule for.
        var activityIds = suggestions.Select(s => s.ActivityId).Distinct().ToList();
        var activityNames = await dbContext.Set<Activity>()
            .AsNoTracking()
            .Where(a => a.UserId == userId && activityIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var sessionsPerActivity = intervals
            .GroupBy(i => i.ActivityId)
            .ToDictionary(g => g.Key, g => g.Select(i => i.SessionId).Distinct().Count());

        var response = suggestions
            .Where(s => activityNames.ContainsKey(s.ActivityId))
            .Select(s => new DesktopMappingSuggestionResponse
            {
                ProcessName = s.Key,
                ProductName = s.Label,
                ActivityId = s.ActivityId,
                ActivityName = activityNames[s.ActivityId],
                SessionCount = s.SessionCount,
                ActivitySessionCount = sessionsPerActivity[s.ActivityId],
                Seconds = s.Seconds
            })
            .ToList();

        await Send.OkAsync(response, ct);
    }
}

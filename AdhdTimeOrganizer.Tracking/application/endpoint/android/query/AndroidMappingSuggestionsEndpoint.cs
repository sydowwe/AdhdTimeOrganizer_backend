using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper.mapping;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.query;

/// <summary>
/// <c>GET /activity-tracking/android/mapping-suggestions?days=30</c> — the android twin of
/// <c>DesktopMappingSuggestionsEndpoint</c> (FO9, F3-02): which uncovered apps the user's own timer focus phases point
/// at one activity. Feeds the "Suggested: Duolingo → Language" chip on the android distinct-entries grid; accepting
/// it is the ordinary rule create, so nothing here writes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The join is time overlap</b>, clipped: a phone session counts only for the seconds it shares with a focus
/// interval, one indexed <c>(UserId, SessionStartUtc)</c> slice per interval. Apps any active rule already covers —
/// ignore rules included — are dropped through <see cref="AndroidMappingRuleSet"/>, the matcher the sync and the
/// grid share.
/// </para>
/// <para>
/// <b>One threshold more than the desktop.</b> A phone in the foreground during a focus timer is as often the
/// distraction as the work, so the app must also fill half the sittings it appeared in
/// (<see cref="MappingSuggestionRule.MinFill"/>); the reasoning is on that class.
/// </para>
/// </remarks>
public class AndroidMappingSuggestionsEndpoint(DbContext dbContext, IFocusIntervalSource focusIntervals)
    : Endpoint<AndroidMappingSuggestionsRequest, List<AndroidMappingSuggestionResponse>>
{
    /// <summary>The newest intervals read, at most — the desktop read's bound, for the same reason.</summary>
    private const int MaxIntervals = 500;

    public override void Configure()
    {
        Get("/activity-tracking/android/mapping-suggestions");
        Summary(s =>
        {
            s.Summary = "Suggest android mapping rules from timer sessions";
            s.Description = "Uncovered apps that the user's timer focus phases attribute to one activity";
            s.Response<List<AndroidMappingSuggestionResponse>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<AndroidMappingSuggestionsValidator>();
    }

    public override async Task HandleAsync(AndroidMappingSuggestionsRequest req, CancellationToken ct)
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

        var rules = new AndroidMappingRuleSet(await AndroidMappingRuleSet.ActiveFor(dbContext, userId).AsNoTracking().ToListAsync(ct));
        var observations = new List<MappingSuggestionRule.Observation>();
        foreach (var interval in intervals)
        {
            var start = DateTime.SpecifyKind(interval.StartUtc, DateTimeKind.Utc);
            var end = DateTime.SpecifyKind(interval.EndUtc, DateTimeKind.Utc);
            var sessions = await dbContext.Set<AndroidSessionData>()
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.SessionStartUtc < end && s.SessionEndUtc > start)
                .Select(s => new { s.PackageName, s.AppLabel, s.SessionStartUtc, s.SessionEndUtc })
                .ToListAsync(ct);

            observations.AddRange(sessions
                .Where(s => rules.Resolve(s.PackageName, s.AppLabel) is null)
                .Select(s => new MappingSuggestionRule.Observation(
                    s.PackageName,
                    s.AppLabel,
                    interval.ActivityId,
                    interval.SessionId,
                    OverlapSeconds(s.SessionStartUtc, s.SessionEndUtc, start, end))));
        }

        // A pomodoro's focus phases are one sitting; the fill threshold divides by all of them.
        var sittingSeconds = intervals
            .GroupBy(i => i.SessionId)
            .ToDictionary(g => g.Key, g => g.Sum(i => (int)(i.EndUtc - i.StartUtc).TotalSeconds));
        var suggestions = MappingSuggestionRule.Suggest(observations, sittingSeconds);
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
            .Select(s => new AndroidMappingSuggestionResponse
            {
                PackageName = s.Key,
                AppLabel = s.Label,
                ActivityId = s.ActivityId,
                ActivityName = activityNames[s.ActivityId],
                SessionCount = s.SessionCount,
                ActivitySessionCount = sessionsPerActivity[s.ActivityId],
                Seconds = s.Seconds
            })
            .ToList();

        await Send.OkAsync(response, ct);
    }

    private static int OverlapSeconds(DateTime sessionStart, DateTime sessionEnd, DateTime start, DateTime end)
    {
        var from = sessionStart > start ? sessionStart : start;
        var to = sessionEnd < end ? sessionEnd : end;
        return Math.Max(0, (int)(to - from).TotalSeconds);
    }
}

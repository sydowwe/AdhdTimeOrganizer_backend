using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.application.service.unified;
using AdhdTimeOrganizer.Tracking.application.validator.unified;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.unified.query;

/// <summary>
/// Attention fragmentation over the merged day — <b>a fifth route beside the three, not a replacement</b>,
/// because the three per-source dashboards stay.
///
/// <para>The response, its nullability, the rule that <c>baseline</c> carries comparable averages and
/// never a <c>percentChange</c>, and the range rule that every measure is computed inside one day's
/// window are all the per-source contract's, unchanged — they come from the shared base rather than
/// from a fourth transcription.</para>
///
/// <para><b>What the merge makes ambiguous, and how it is settled.</b> Sessions are keyed on the
/// unified <c>label</c>, not on the source, and that single decision answers both questions the merged
/// view raises:</para>
/// <list type="bullet">
/// <item><b>A switch is a change of label in the merged, de-overlapped stream, whatever the source
/// either side came from.</b> Putting the laptop down and picking up the phone <i>is</i> a switch —
/// that is exactly the fragmentation the merged view exists to show and no per-source dashboard can
/// see it.</item>
/// <item><b>Consecutive sessions on the same label from different sources are not.</b> Slack on the
/// desktop then Slack on the phone is a device change, not a change of what is being attended to, and
/// counting it would make the merged switch count read as worse attention than the reality.</item>
/// </list>
///
/// <para>By the same reading <c>longestBlock</c> may span sources: a run on one label continues across
/// a device change, subject to the same <c>focusGapSeconds</c> tolerance.</para>
/// </summary>
public class UnifiedFocusMetricsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : BaseFocusMetricsEndpoint<UnifiedFocusMetricsRequest>(timeZones)
{
    public override void Configure() =>
        ConfigureFocusMetrics<UnifiedFocusMetricsValidator>("/activity-tracking/unified/focus-metrics", "merged");

    /// <summary>
    /// One primary stream per day, built from the minutes each source owns outright.
    ///
    /// <para>The exclusive partition rather than the shares is what a session stream needs: two sources
    /// holding parts of one minute would produce two overlapping sessions in the same stream, and every
    /// measure downstream — switches, blocks, the interior gap — reads a stream in time order.</para>
    /// </summary>
    protected override async Task<IReadOnlyList<IReadOnlyList<FocusSession>>> LoadAsync(
        UnifiedFocusMetricsRequest req, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var span = await UnifiedSpan.BuildAsync(db, userId, windows, req.SelectedSources(), ct);

        var byDay = Bucketize(span.ExclusiveMinutes(), windows, m => m.Minute);

        return byDay
            .Select(day => (IReadOnlyList<FocusSession>)TimelineSegmentBuilder
                .Build(day.Select(m => new ActivityMinute(
                    m.Minute, m.Label, null, (int)Math.Round(m.Seconds, MidpointRounding.AwayFromZero))))
                .Primary
                .Select(s => new FocusSession(s.Key, s.Key, s.StartedAt, s.EndedAt, s.DurationSeconds))
                .ToList())
            .ToList();
    }

    protected override Task<DateOnly?> FirstActivityDayAsync(
        UnifiedFocusMetricsRequest req, long userId, TimeZoneInfo timeZone, CancellationToken ct) =>
        UnifiedActivityLoader.FirstActivityDayAsync(db, userId, req.SelectedSources(), timeZone, ct);
}

using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.unified;

/// <summary>
/// The source selection, as an interface so the validation rule is written once.
///
/// <para>It is an interface rather than a shared base because <c>focus-metrics</c> already has a base —
/// <c>FocusMetricsRequest</c>, whose <c>baseline</c> and <c>focusGapSeconds</c> the unified route
/// carries unchanged — and C# has one of those to give.</para>
/// </summary>
public interface IUnifiedSourceSelection
{
    List<string> Sources { get; }
}

/// <summary>
/// The base the six unified dashboards bind: the same span and time-of-day window as every other
/// tracking dashboard, plus the set of trackers the answer is to be computed over.
///
/// <para><b><see cref="Sources"/> is a request field, not a client-side filter, and every change of it
/// is a round trip.</b> That is the whole reason these endpoints exist rather than three responses
/// merged in the browser. An hour in a browser is attributed to the web extension while the desktop
/// agent is also selected; turn the extension off and that hour has to come <i>back</i> to the desktop
/// agent as <c>Google Chrome</c> — not vanish, and not stay credited to a source that is no longer on
/// screen. A client filtering a pre-merged payload can only hide a lane; it cannot give the time
/// back.</para>
///
/// <para>Bound as strings rather than as an enum on purpose — see
/// <see cref="TrackingSourceNames"/>.</para>
/// </summary>
public record UnifiedDashboardRequest : DateRangeAndTimeRangeDto, IUnifiedSourceSelection
{
    /// <summary>
    /// Non-empty; each member one of <c>webExtension</c>, <c>desktop</c>, <c>android</c>. Duplicates
    /// collapse and order is ignored — the client sends it in precedence order whether the selection
    /// was built by clicking or parsed from a shared link, but it is a set either way.
    /// </summary>
    public List<string> Sources { get; init; } = [];

    /// <summary>The selection as a set. Read after validation, which has already rejected the empty and unknown cases.</summary>
    public IReadOnlySet<TrackingSource> SelectedSources() => TrackingSourceNames.ParseSet(Sources);
}

/// <summary>Merged <c>summary-cards</c>. <see cref="Baseline"/> reads exactly as it does per source.</summary>
public record UnifiedSummaryCardsRequest : UnifiedDashboardRequest
{
    public int? TopN { get; init; }

    public BaselineType Baseline { get; init; } = BaselineType.Last7Days;
}

/// <summary>Merged <c>pie-chart</c>.</summary>
public record UnifiedPieChartRequest : UnifiedDashboardRequest
{
    public double? MinPercent { get; init; }
}

/// <summary>Merged <c>stacked-bars</c>. The tiling rule is <c>DailyWindowSet.Tile</c>'s, unchanged — the same client component draws both.</summary>
public record UnifiedStackedBarsRequest : UnifiedDashboardRequest
{
    public required int WindowMinutes { get; init; }
}

/// <summary>
/// Merged <c>timeline</c>. Single day only, exactly as the three per-source timelines are: a merged
/// month of sessions is even less legible than one source's, and the client falls back to the stacked
/// bars over a range through the same code path.
/// </summary>
public record UnifiedTimelineRequest : UnifiedDashboardRequest;

/// <summary>
/// Merged <c>focus-metrics</c> — a fifth route beside the three, not a replacement, because the three
/// per-source dashboards stay.
/// </summary>
public record UnifiedFocusMetricsRequest : FocusMetricsRequest, IUnifiedSourceSelection
{
    /// <inheritdoc cref="UnifiedDashboardRequest.Sources"/>
    public List<string> Sources { get; init; } = [];

    public IReadOnlySet<TrackingSource> SelectedSources() => TrackingSourceNames.ParseSet(Sources);
}

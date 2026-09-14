using AdhdTimeOrganizer.Core.infrastructure.persistence;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.History.application.service;

/// <summary>
/// The one thing every history <em>read</em> has to say out loud: archiving an activity retires it from the
/// pickers, it does not erase what the user already did.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists as a named call rather than a filter on the entity.</b> <c>ActivityHistory</c> carries
/// no archived filter — correctly, because the rows are the record of time already spent. <c>Activity</c>
/// does, and every history read that wants a name, a role or a colour reaches it through a <b>required</b>
/// navigation. A filtered principal on a required navigation does not yield a row with empty fields; it
/// yields <b>no row</b>, whether it is reached by projection or by <c>Include</c>. So the archived activity's
/// sessions silently left the detail list, the calendar and all six dashboards, while the surfaces that never
/// touch <c>Activity</c> — time-of-day, aggregate-by-activity, and the summary cards' own previous-period
/// total — went on counting them. One page showed 30 minutes in the pie and an hour in the time-of-day chart,
/// out of the same range, with a 200 on both.
/// </para>
/// <para>
/// Call this on every read whose answer is about time that has passed. Do <b>not</b> call it on a picker:
/// <c>FormSelectOptionsActivityHistoryEndpoint</c> feeds a dropdown of activities to log against, and an
/// archived activity is precisely what should not be offered there.
/// </para>
/// <para>
/// Named-filter lift only. The argument-less <c>IgnoreQueryFilters()</c> would also drop
/// <see cref="QueryFilters.UserScope"/>, which is the only thing standing between these reads and every
/// user's history.
/// </para>
/// </remarks>
public static class ActivityHistoryReadExtensions
{
    private static readonly string[] ArchivedActivity = [AppQueryFilters.ActivityArchived];

    /// <inheritdoc cref="ActivityHistoryReadExtensions"/>
    public static IQueryable<ActivityHistory> IncludingArchivedActivities(this IQueryable<ActivityHistory> query) =>
        query.IgnoreNamedQueryFilters(ArchivedActivity);

    /// <summary>
    /// The filter names a get-by-id style endpoint declares to reach the same rows — see
    /// <c>BaseGetByIdEndpoint.IgnoredQueryFilters</c>, which takes the names rather than the query.
    /// </summary>
    public static string[] ArchivedActivityFilter => ArchivedActivity;
}

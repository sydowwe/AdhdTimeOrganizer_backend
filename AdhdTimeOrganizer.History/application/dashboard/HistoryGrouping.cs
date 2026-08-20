using AdhdTimeOrganizer.History.application.dto.@enum;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.History.application.dashboard;

/// <summary>
/// The identity of one dashboard group: the id of the entity the row was grouped by, plus the name and
/// colour the group renders with.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Id"/> is what groups, not <see cref="Name"/>.</b> The name used to be, and that worked only
/// by leaning on something this code cannot see: <c>Activity</c>, <c>ActivityRole</c> and
/// <c>ActivityCategory</c> each carry an unfiltered unique index on <c>(UserId, Name)</c>, and every
/// dashboard is user-scoped, so within one response no two groups could share a name. Relax any one of those
/// three indexes — the archiving lifecycle already wants to, since a retired "Reading" holds its name against
/// its replacement forever — and name-keyed grouping folds two unrelated groups into one slice, one card and
/// one bar segment, with their totals silently added. It fails as a wrong number, not as an error.
/// </para>
/// <para>
/// Two things are wrong today regardless of that index. A group's identity is not stable across a rename, so
/// a client holding an earlier response reads a renamed group as one that vanished and a new one that
/// appeared; and the synthetic <c>_other</c> and <c>Uncategorized</c> buckets are named strings a user is
/// free to name a real activity after, which no name-keyed client can tell apart. An id fixes both, and lets
/// a client drill from a chart into a view filtered by the group at all.
/// </para>
/// <para>
/// <see cref="Id"/> is <c>null</c> for the one group that is not an entity: the <c>Uncategorized</c> bucket
/// that collects activities with no category. The pie chart's <c>_other</c> roll-up is likewise emitted with
/// a null id. Those two are keyed by name downstream, which is safe because both are synthesized here and
/// there is exactly one of each per response.
/// </para>
/// <para>
/// This is a value-equality type on purpose — <c>GroupBy</c> and the summary cards' baseline lookup both
/// compare whole keys, so a colour or name that differs between two rows sharing an id can never split a
/// group without also being visible here.
/// </para>
/// </remarks>
public readonly record struct HistoryGroupKey(long? Id, string Name, string? Color);

public static class HistoryGrouping
{
    /// <summary>The name of the synthetic bucket for activities that have no category.</summary>
    public const string UncategorizedName = "Uncategorized";

    /// <summary>
    /// Requires <c>Activity</c>, <c>Activity.Role</c> and <c>Activity.Category</c> to be loaded — every
    /// dashboard endpoint <c>Include</c>s them and then groups in memory.
    /// </summary>
    public static HistoryGroupKey ResolveGroupKey(this ActivityHistory ah, HistoryGroupBy groupBy)
    {
        return groupBy switch
        {
            HistoryGroupBy.Role => new HistoryGroupKey(ah.Activity.RoleId, ah.Activity.Role.Name, ah.Activity.Role.Color),
            HistoryGroupBy.Category => ah.Activity.Category != null
                ? new HistoryGroupKey(ah.Activity.CategoryId, ah.Activity.Category.Name, ah.Activity.Category.Color)
                : new HistoryGroupKey(null, UncategorizedName, null),
            _ => new HistoryGroupKey(ah.ActivityId, ah.Activity.Name, null)
        };
    }
}

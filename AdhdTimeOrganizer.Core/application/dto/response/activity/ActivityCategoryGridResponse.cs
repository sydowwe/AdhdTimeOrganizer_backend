using System.Text.Json.Serialization;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.dto.response;

namespace AdhdTimeOrganizer.Core.application.dto.response.activity;

/// <summary>
/// The settings grid's view of a category. Adds the roles the category is used under, which the plain
/// <see cref="ActivityCategoryResponse"/> cannot carry: a category has no role of its own, it reaches
/// roles only through the activities filed under it.
/// </summary>
public record ActivityCategoryGridResponse : ActivityCategoryResponse, IProjectionResponse<ActivityCategoryGridResponse, ActivityCategory>
{
    /// <summary>
    /// Every distinct role worn by the activities in this category, alphabetical and comma-joined
    /// (<c>"Personal, Work"</c>); empty when the category has no activities. Archived activities do not
    /// count — the <c>ActivityArchived</c> query filter reaches into this subquery too.
    /// </summary>
    /// <remarks>
    /// <b>This property cannot be sorted on.</b> Npgsql does not turn <c>string.Join</c> into
    /// <c>string_agg</c>: EF renders the subquery as a <c>LEFT JOIN LATERAL</c> yielding one row per role
    /// — the <c>DISTINCT</c> and the ordering do happen in SQL — and then concatenates the rows in
    /// memory once they are materialized. That is fine in a projection and impossible in an
    /// <c>ORDER BY</c>, where it fails as an untranslatable LINQ expression rather than falling back to
    /// anything. <see cref="RoleSortKey"/> is what the grid sorts by instead.
    /// </remarks>
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// The sort proxy for <see cref="Role"/>: the alphabetically first role name, which is exactly the
    /// leading segment of the joined string, so ordering by it orders the column as displayed. Unlike
    /// the join this is a real aggregate and translates to a correlated <c>min(...)</c> subquery.
    /// <c>null</c> for a category with no activities, which Postgres sorts last ascending.
    /// <c>GridActivityCategoryEndpoint.PreprocessSortBy</c> maps the incoming <c>role</c> sort key onto it,
    /// so the client keeps sorting by the column it can see.
    /// </summary>
    [JsonIgnore]
    public string? RoleSortKey { get; init; }

    public new static IQueryable<ActivityCategoryGridResponse> Projection(IQueryable<ActivityCategory> query)
    {
        return query.Select(e => new ActivityCategoryGridResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            Color = e.Color,
            Icon = e.Icon,
            Role = string.Join(", ", e.Activities.Select(a => a.Role.Name).Distinct().OrderBy(n => n)),
            RoleSortKey = e.Activities.Select(a => a.Role.Name).Min()
        });
    }
}

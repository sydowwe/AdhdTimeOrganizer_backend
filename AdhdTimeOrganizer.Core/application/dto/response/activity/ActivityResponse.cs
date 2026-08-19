using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.Core.application.dto.response.activity;

public record ActivityResponse : NameTextResponse, IProjectionResponse<ActivityResponse, Activity>
{
    public bool IsOnTodoList { get; init; }
    public bool IsUnavoidable { get; init; }

    /// <summary>Retired: keeps all its history, disappears from every picker.</summary>
    public bool IsArchived { get; init; }

    /// <summary>
    /// How many rows, across every referencing entity type, point at this activity.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Zero here does not mean "no references" unless the endpoint filled it in.</b> The static
    /// <see cref="Projection"/> cannot reach the seam — it takes only an <c>IQueryable&lt;Activity&gt;</c>,
    /// and eight of the twelve referencing tables live in slices Core cannot see. Only
    /// <c>GridActivityEndpoint</c> (which overrides the projection so the column stays sortable) and
    /// <c>GetByIdActivityEndpoint</c> populate it; everywhere else this is the default and means
    /// "not asked for". That matches the contract the ask sets out — the field is required on those two
    /// endpoints and explicitly optional on the nested activity payloads that planner tasks, to-do items,
    /// history rows, timer presets and tracking mappings carry, where a join per row would be real cost
    /// for a number nothing renders.
    /// </remarks>
    public int UsageCount { get; init; }

    /// <summary>
    /// A hard delete would succeed without destroying anything — i.e. nothing references this activity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Today this is exactly <c>UsageCount == 0</c>: nothing is deliberately excluded from the count, so
    /// the two never diverge. They are two fields anyway because they answer different questions and the
    /// UI uses them differently — <c>usageCount</c> is a number it displays and sorts and predicts merge
    /// results from, <c>canDelete</c> gates a destructive button. If a reference type is ever dropped
    /// from the count (presets are the only plausible candidate), this is the one that must keep
    /// counting it.
    /// </para>
    /// <para>
    /// ⚠ It is <b>not</b> a claim that the FK would refuse. Every activity FK in the solution is
    /// <c>DeleteBehavior.Cascade</c>, so <c>DELETE /activity/{id}</c> on a referenced activity returns
    /// 204 and takes the history with it. <c>canDelete</c> is the guard that stops the UI offering that,
    /// not a prediction of a 409 the database would never raise.
    /// </para>
    /// <para>
    /// Same "default means not asked for" caveat as <see cref="UsageCount"/> — and the default is
    /// <c>false</c>, chosen so a missing value never offers a delete.
    /// </para>
    /// </remarks>
    public bool CanDelete { get; init; }

    public required ActivityRoleResponse Role { get; init; }
    public ActivityCategoryResponse? Category { get; init; }

    public static IQueryable<ActivityResponse> Projection(IQueryable<Activity> query)
    {
        return query.Select(e => new ActivityResponse
        {
            Id = e.Id,
            Name = e.Name,
            Text = e.Text,
            IsUnavoidable = e.IsUnavoidable,
            IsArchived = e.IsArchived,
            Role = new ActivityRoleResponse { Id = e.Role.Id, Name = e.Role.Name, Text = e.Role.Text, Color = e.Role.Color, Icon = e.Role.Icon },
            Category = e.Category == null ? null : new ActivityCategoryResponse { Id = e.Category.Id, Name = e.Category.Name, Text = e.Category.Text, Color = e.Category.Color, Icon = e.Category.Icon }
        });
    }
}
